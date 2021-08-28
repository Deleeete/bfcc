using bfcc;
using System;
using System.Diagnostics;
using System.IO;
namespace bfc.net
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0) Compiler.PrintHelp();
            else
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();
                try
                {
                    Console.Write($"解析参数...");
                    Compiler.ParseArgs(args);
                    Konsole.WriteLnSuccess();
                    Console.WriteLine("配置：");
                    Console.WriteLine($"   |结束后挂起 = {Compiler.Pause}");
                    Console.WriteLine($"   |调试模式 = {Compiler.Pause}");
                    Console.WriteLine($"   |缓冲区大小 = {Compiler.BufferSize}");
                    Console.WriteLine($"   |优化级别 = {Compiler.OptimizeLevel}");
                }
                catch (Exception ex) { Konsole.WriteLnError($"参数解析失败：{ex.Message}"); goto end; }

                string[] lines;
                try
                {
                    Console.Write($"读取文件\"{Compiler.SourceName}\"...");
                    lines = File.ReadAllLines(Compiler.SourceName);
                    Konsole.WriteLnSuccess();
                }
                catch (Exception ex) { Konsole.WriteLnError("文件读取失败：" + ex.Message); goto end; }

                try
                {
                    Compiler.Initialize();
                    Compiler.Parse(lines);
                    Compiler.Compile();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"编译结束 -> {Compiler.OutputName}");
                    Console.ResetColor();
                    return;
                }
                catch (Exception ex)
                {
                    if (Compiler.CurrentStatus == Status.BUILD) Konsole.WriteLnError($"表达式构建失败：{ex.Message}");
                    else if (Compiler.CurrentStatus == Status.PARSE) Konsole.WriteLnError($"源码解析失败：{ex.Message}   行号：{Compiler.Data.Row}，位置：{Compiler.Data.Col}");
                    else if (Compiler.CurrentStatus == Status.COMPILE) Konsole.WriteLnError($"模型编译失败：{ex.Message}");
                    goto end;
                }
                end: Console.WriteLine("\n*****发生异常，已退出*****\n");
            }
        }

    }
}
