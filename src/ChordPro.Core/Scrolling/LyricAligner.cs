using System.Globalization;
using System.Text;

namespace ChordPro.Core.Scrolling;

/// <summary>
/// Follows a song by its words: given the words heard in the last few seconds of singing (from
/// speech recognition, which gets sung words partly wrong), finds the lyric line being sung. The
/// song's own lyrics are the reference, so a handful of words heard right is enough to place it.
/// Only the lyrics just around and ahead of the current line are searched, which is how a repeated
/// chorus is told from the first one, and recognition errors far away in the song are ignored.
/// Lines that repeat ("hold on, hold on" four times) fit the words heard equally well at every copy;
/// the aligner learns how fast the words are being sung and takes the copy the singer should have
/// reached by now.
/// </summary>
public sealed class LyricAligner
{
    // How far a match may be from the current place, in words: a little back, to recover from
    // jumping ahead too soon (and back to the start of the current section, for a chorus sung again
    // that's written once), and a couple of verses forward, for a skipped or short section.
    private const int LookBack = 12;
    private const int LookAhead = 80;

    // Local alignment scores: a word heard right, nearly right ("great" for "grace"), and the
    // cost of a wrong, missing or extra word.
    private const double Exact = 2, Close = 1, Miss = -1;

    // A place is only trusted on this much evidence, so a stray word or two doesn't move it. The
    // number of words can be tuned (see VoiceTuning): a clear singer is placed sooner on two.
    private const double MinScore = 5;
    private readonly int _minWords;

    // Singing pace in words per second: a typical starting guess, then learned from what's heard.
    private const double StartingPace = 2;
    // A pause (an instrumental) doesn't move the singer on, so expect at most this much progress.
    private const double MaxExpectedSeconds = 5;
    // Of a pause in the singing, this much counts towards the pace: a breath between lines does,
    // a long instrumental doesn't.
    private const double PauseCounted = 1;
    private const double SlowestPace = 0.8;

    private readonly List<(string Word, int Line)> _words = [];
    private readonly List<(Section Section, int LineIndex)> _lines = [];
    private readonly List<string> _lineText = [];
    private readonly List<int> _sectionStart = [];   // per line: the index of its section's first word
    private int _position = -1;
    // Where the singer is reckoned to be, in words, and when: set by a clear match, and moved on by
    // the pace through repeats. It isn't rounded to a matching word, so small errors don't add up.
    private double _reckoned = -1;
    private double _reckonedTime = double.NaN;
    // The last clear match, for learning the pace.
    private (int Position, double Time) _clear = (-1, double.NaN);
    private double _pace = StartingPace;

    /// <param name="minWords">How many words must match before a place is trusted.</param>
    public LyricAligner(Song song, int minWords = VoiceTuning.DefaultMinWords)
    {
        _minWords = Math.Max(2, minWords);
        foreach (var section in song.Sections.Where(s => s.Kind is not (SectionKind.Tab or SectionKind.Grid)))
        {
            for (var i = 0; i < section.Lines.Count; i++)
            {
                if (section.Lines[i] is not LyricLine line)
                    continue;
                var text = string.Concat(line.Segments.Select(s => s.Lyric));
                var words = Words(text);
                if (words.Count == 0)
                    continue;
                _lines.Add((section, i));
                _lineText.Add(text.Trim());
                _sectionStart.Add(_lines.Count > 1 && _lines[^2].Section == section ? _sectionStart[^1] : _words.Count);
                foreach (var word in words)
                    _words.Add((word, _lines.Count - 1));
            }
        }
    }

    /// <summary>The lyric lines in order, as (section, index of the line in it).</summary>
    public IReadOnlyList<(Section Section, int LineIndex)> Lines => _lines;

    /// <summary>Every lyric word in order (normalized as by <see cref="Words"/>), with the index of its line in <see cref="Lines"/>.</summary>
    public IReadOnlyList<(string Word, int Line)> LyricWords => _words;

    /// <summary>The index in <see cref="Lines"/> of the line last matched, or -1 before anything was heard.</summary>
    public int CurrentLineNumber => _position < 0 ? -1 : _words[_position].Line;

    /// <summary>
    /// The lyrics sung just before the current line, for prompting speech recognition. Whisper takes
    /// a prompt as what was said before the audio, so this must not include the line being sung
    /// (given upcoming words, it assumes they're already said and leaves them out). Null before
    /// anything was heard.
    /// </summary>
    public string? PromptText(int lines = 2)
    {
        if (_position < 0)
            return null;
        var line = _words[_position].Line;
        var text = string.Join(" ", _lineText.Skip(Math.Max(0, line - lines)).Take(line - Math.Max(0, line - lines)));
        return text.Length == 0 ? null : text;
    }

    /// <summary>The line last matched, or null before anything was heard.</summary>
    public (Section Section, int LineIndex)? CurrentLine => _position < 0 ? null : _lines[_words[_position].Line];

    /// <summary>How far through <see cref="CurrentLine"/> the last matched word is, from just above 0 to 1.</summary>
    public double CurrentFraction
    {
        get
        {
            if (_position < 0)
                return 0;
            var line = _words[_position].Line;
            var first = _words.FindIndex(w => w.Line == line);
            var count = _words.Count(w => w.Line == line);
            return (_position - first + 1) / (double)count;
        }
    }

    /// <summary>
    /// Places the words heard in the latest stretch of audio. Returns the line of the last word that
    /// matched, or null when nothing matched well enough (an instrumental, or recognition too far off),
    /// in which case the current place is kept.
    /// </summary>
    /// <param name="time">When the words were heard, in seconds on any steady clock; NaN if unknown.</param>
    public (Section Section, int LineIndex)? Hear(string transcript, double time = double.NaN)
    {
        var heard = Words(transcript);
        if (heard.Count < _minWords || _words.Count == 0)
            return null;

        var (from, to) = _position < 0 ? (0, _words.Count)
            : (Math.Min(Math.Max(0, _position - LookBack), _sectionStart[_words[_position].Line]), Math.Min(_words.Count, _position + 1 + LookAhead));
        var expected = _position < 0 || double.IsNaN(time) || double.IsNaN(_reckonedTime)
            ? _position
            : _reckoned + _pace * Math.Clamp(time - _reckonedTime, 0, MaxExpectedSeconds);
        // However long it's been, the singer can't be further on than the words just heard reach
        // (recognition misses about one word in four), which matters coming out of a pause.
        if (_position >= 0)
            expected = Math.Min(expected, _position + heard.Count * 1.3 + 1);
        var best = (Score: double.MinValue, End: -1);
        var bestRaw = double.MinValue;   // the best score before the pull towards the expected place
        var rawEnds = new List<(double Score, int End)>();

        // Smith-Waterman local alignment of the heard words against the lyrics in range, keeping the
        // best place each heard stretch could end, with a small pull towards the current position.
        var previous = new (double Score, int Matched)[heard.Count + 1];
        for (var j = from; j < to; j++)
        {
            var current = new (double Score, int Matched)[heard.Count + 1];
            for (var i = 1; i <= heard.Count; i++)
            {
                var similarity = Similarity(heard[i - 1], _words[j].Word);
                var diagonal = previous[i - 1].Score + (similarity > 0 ? similarity : Miss);
                var cell = (Score: 0.0, Matched: 0);
                if (diagonal > cell.Score)
                    cell = (diagonal, previous[i - 1].Matched + (similarity > 0 ? 1 : 0));
                if (previous[i].Score + Miss > cell.Score)
                    cell = (previous[i].Score + Miss, previous[i].Matched);
                if (current[i - 1].Score + Miss > cell.Score)
                    cell = (current[i - 1].Score + Miss, current[i - 1].Matched);
                current[i] = cell;

                if (cell.Matched >= _minWords && cell.Score >= Math.Min(MinScore, _minWords * Exact) && similarity > 0)
                {
                    var ranked = cell.Score - Distance(j, expected);
                    if (ranked > best.Score)
                        best = (ranked, j);
                    rawEnds.Add((cell.Score, j));
                    bestRaw = Math.Max(bestRaw, cell.Score);
                }
            }
            previous = current;
        }

        if (best.End < 0)
            return null;

        // Learn the pace, but only from a place the words alone decided: where they fit clearly
        // better than anywhere else. A place picked among equal repeats was picked by the pace.
        var clear = rawEnds.Where(e => e.Score >= bestRaw - 1).Select(e => e.End).Distinct().Count() <= 2;
        // The same place again: the window still holds words already placed, so nothing new has
        // been sung. Stop the clock rather than reckon the singer on, and count only a little of
        // the pause towards the pace.
        if (clear && best.End == _position && !double.IsNaN(time))
        {
            _reckonedTime = time;
            if (_clear.Position == best.End)
                _clear.Time = Math.Max(_clear.Time, time - PauseCounted);
            return _lines[_words[_position].Line];
        }
        if (clear && _clear.Position >= 0 && !double.IsNaN(time) && time - _clear.Time is > 0.5 and < MaxExpectedSeconds)
        {
            var observed = (best.End - _clear.Position) / (time - _clear.Time);
            if (observed is >= 0 and < 5)
                _pace = Math.Max(SlowestPace, 0.7 * _pace + 0.3 * observed);
        }
        if (clear)
            _clear = (best.End, time);
        _position = best.End;
        (_reckoned, _reckonedTime) = (clear || double.IsNaN(time) ? best.End : expected, time);
        return _lines[_words[_position].Line];
    }

    /// <summary>Starts following from the beginning of <paramref name="line"/>, e.g. after the view was scrolled by hand.</summary>
    public void MoveTo(Section section, int lineIndex)
    {
        var line = _lines.IndexOf((section, lineIndex));
        if (line < 0)
            return;
        _position = Math.Max(-1, _words.FindIndex(w => w.Line == line) - 1);
        (_reckoned, _reckonedTime, _clear) = (_position, double.NaN, (-1, double.NaN));
    }

    /// <summary>Forgets the current place, so the next words are looked for in the whole song.</summary>
    public void Reset() => (_position, _reckoned, _reckonedTime, _clear) = (-1, -1, double.NaN, (-1, double.NaN));

    // The pull towards where the singer should be: the further from it, the less likely; and going
    // back behind the last place costs more again, as singing moves forward.
    private double Distance(int index, double expected)
    {
        if (_position < 0)
            return 0;
        var behind = index < _position ? (_position - index) * 0.25 : 0;
        return Math.Abs(index - expected) * 0.08 + behind;
    }

    /// <summary>
    /// The words of a lyric or transcript, lower-case, without punctuation: "'Twas grace," and
    /// "twas grace" are the same two words, and "hallelu-jah" one.
    /// </summary>
    public static List<string> Words(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
                word.Append(char.ToLowerInvariant(c));
            else if (c is '\'' or '’' or '-' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue; // joins: "don't" -> "dont", "hallelu-jah" -> "hallelujah", "é" -> "e"
            else if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }
        if (word.Length > 0)
            words.Add(word.ToString());
        return words;
    }

    // Exact, close (most letters right, as recognition gets sung words), or no match (0).
    internal static double Similarity(string heard, string lyric)
    {
        if (heard == lyric)
            return Exact;
        var longer = Math.Max(heard.Length, lyric.Length);
        if (longer < 4)
            return 0; // short words ("a", "I", "the") are only evidence when right
        return 1 - EditDistance(heard, lyric) / (double)longer >= 0.6 ? Close : 0;
    }

    private static int EditDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[b.Length];
    }
}
