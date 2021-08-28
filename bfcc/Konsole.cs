using System;

namespace bfcc
{
    public static class Konsole
    {
        public static void WriteLnSuccess()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[成功]");
            Console.ResetColor();
        }
        public static void WriteLnError(string str)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[错误] \n" + str);
            Console.ResetColor();
        }

    }
}
