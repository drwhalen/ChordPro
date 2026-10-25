using UIKit;

namespace ChordPro.Mobile;

public static class Program
{
    public static void Main(string[] args)
    {
        ErrorLog.Install();
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
