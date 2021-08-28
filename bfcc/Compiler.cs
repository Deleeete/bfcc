using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace bfcc
{
    public static class Compiler
    {
        public static string SourceName { get; set; }
        public static string OutputName { get; set; } = "a.exe";
        public static int BufferSize { get; set; } = 65536;
        public static bool Pause { get; set; } = false;
        public static bool Debug { get; set; } = false;
        public static Optimize OptimizeLevel { get; set; } = Optimize.NONE;
        public static Status CurrentStatus { get; set; }
        public static List<Expression> Exprs { get; } = new List<Expression>();
        public static Exception UnknownOption(string option_name, string content)
        {
            return new Exception($"未知的{option_name}选项'{content}'");
        }
        static bool IsPtrCache { get => OptimizeLevel == Optimize.PTR || OptimizeLevel == Optimize.BOTH; }
        static bool IsValueCache { get => OptimizeLevel == Optimize.VALUE || OptimizeLevel == Optimize.BOTH; }

        #region Expressions
        static ParameterExpression pointer_exp = Expression.Variable(typeof(int), "pointer");
        static ParameterExpression buffer_exp = Expression.Variable(typeof(ushort[]), "buffer");
        //获取当前值
        static IndexExpression current_exp = Expression.ArrayAccess(buffer_exp, pointer_exp);
        //当前值为0？
        static BinaryExpression is0_exp = Expression.Equal(current_exp, Expression.Constant((ushort)0, typeof(ushort)));
        //当前值不为0？
        static BinaryExpression is_not0_exp = Expression.NotEqual(current_exp, Expression.Constant((ushort)0, typeof(ushort)));
        #endregion
        #region MethodInfos
        static MethodInfo write_char = typeof(Console).GetMethod("Write", new Type[] { typeof(char) });
        static MethodInfo write_string = typeof(Console).GetMethod("Write", new Type[] { typeof(string) });
        static MethodInfo readkey = typeof(Console).GetMethod("ReadKey", new Type[] { typeof(bool) });
        static MethodInfo ushort_tostring = typeof(Convert).GetMethod("ToString", new Type[] { typeof(ushort) });
        static MethodInfo int_tostring = typeof(Convert).GetMethod("ToString", new Type[] { typeof(int) });
        static MethodInfo concat = typeof(string).GetMethod("Concat", new Type[] { typeof(string), typeof(string) });
        #endregion
        static int ptr_cache = 0, value_cache = 0;  //指针位移缓存，值加减缓存

        public static void PrintHelp()
        {
            Console.WriteLine("bfcc - Brainfuck CLR Compiler - KLG SOFTWARE DEVELOPMENT\n");
            Console.WriteLine(" 用法：bfcc  [参数(可选)]  [源文件名]  [输出文件名(可选)]");
            Console.WriteLine("  |  使用源文件编译，输出到指定文件，默认为'a.exe'");
            Console.WriteLine("  |  使用的字长为16位（而非标准Brainfuck8位ASCII），因此编译的程序可以打印Unicode字符");
            Console.WriteLine(" 参数：");
            Console.WriteLine("  |  --buffer-size [SIZE]      设置Brainfuck缓冲区长度，默认为65536");
            Console.WriteLine("  |  --debug                   启用此参数后，可以在源码中使用'?'以数字形式打印当前格的值。默认关闭");
            Console.WriteLine("  |  --pause                   启用此参数后，编译后的程序执行完不会自动退出。默认关闭");
            Console.WriteLine("  |  --optimize [MODE]         设置优化模式，默认为NONE。可选的值有：");
            Console.WriteLine("  |               |   NONE           优化关闭");
            Console.WriteLine("  |               |   PTR            打开指针位移的编译时缓存");
            Console.WriteLine("  |               |   VALUE          打开值累加的编译时缓存");
            Console.WriteLine("  |               |   BOTH           打开所有编译时缓存");
            Console.WriteLine("");
        }
        public static void ParseArgs(string[] args)
        {
            //先读参数
            var list = args.ToList();
            while (list.Count != 0 && list[0].StartsWith("--"))
            {
                if (list[0] == "--buffer-size")
                {
                    if (list.Count() < 2) throw CompilerException.lackEx;
                    BufferSize = int.Parse(list[1]);
                    list.RemoveRange(0, 2);
                }
                else if (list[0] == "--optimize")
                {
                    if (list.Count() < 2) throw CompilerException.lackEx;
                    if (list[1].ToUpper() == "NONE") OptimizeLevel = Optimize.NONE;
                    else if (list[1].ToUpper() == "PTR") OptimizeLevel = Optimize.PTR;
                    else if (list[1].ToUpper() == "VALUE") OptimizeLevel = Optimize.VALUE;
                    else if (list[1].ToUpper() == "BOTH") OptimizeLevel = Optimize.BOTH;
                    else
                    {
                        Console.WriteLine();
                        PrintHelp();
                        throw UnknownOption("优化", list[1]);
                    }
                    list.RemoveRange(0,2);
                }
                else if (list[0] == "--pause")
                {
                    Pause = true;
                    list.RemoveAt(0);
                }
                else if (list[0] == "--debug")
                {
                    Debug = true;
                    list.RemoveAt(0);
                }
                else
                {
                    Console.WriteLine();
                    PrintHelp();
                    throw new Exception($"未知参数{list[0]}");
                } 
            }
            //再读文件名
            if (list.Count == 0) throw CompilerException.srcEx;
            else
            {
                SourceName = list[0];
                list.RemoveAt(0);
            }
            if (list.Count != 0)
            {
                OutputName = list[0];
                list.RemoveAt(0);
            }
            if (list.Count != 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write($"[警告] '{list[0]}'及之后的任何参数都将被忽略");
                Console.ResetColor();
                Console.Write("...");
            }
        }
        public static void Initialize()
        {
            CurrentStatus = Status.BUILD;
            Console.Write($"定义指针初始化表达式...");
            var pointer_assign_exp = Expression.Assign(pointer_exp, Expression.Constant(0));
            Konsole.WriteLnSuccess();
            Console.Write($"定义缓冲区初始化表达式，缓冲区大小：{BufferSize}  ...");
            var buffer_assign_exp = Expression.Assign(buffer_exp,
                Expression.NewArrayBounds(
                        typeof(ushort),
                        Expression.Constant(BufferSize)));
            Konsole.WriteLnSuccess();
            Console.Write("装载初始化表达式...");
            Exprs.Add(pointer_assign_exp);
            Exprs.Add(buffer_assign_exp);
            Konsole.WriteLnSuccess();

        }
        public static void Parse(string[] lines)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            CurrentStatus = Status.PARSE;
            Data.TotalExprs = Exprs.Count();
            int depth = 0;//循环嵌套深度
            List<Point> pair = new List<Point>();
            Stack<int> stack = new Stack<int>();
            Console.WriteLine(" >>开始解析...>>");
            for (int row = 0; row < lines.Length; row++)
            {
                char[] chrs = lines[row].ToCharArray();
                for (int col = 0; col < chrs.Length; col++)
                {
                    Data.TotalChars++;
                    Data.Col = col + 1;
                    Data.Row = row + 1;
                    if (chrs[col] == '?')
                    {
                        Data.TotalChars++;
                        ClearValueCache();
                        ClearPtrCache();
                        var concat_0 = Expression.Call(concat, Expression.Constant("["), Expression.Call(int_tostring, pointer_exp));
                        var concat_1 = Expression.Call(concat, concat_0, Expression.Constant(":"));
                        var concat_2 = Expression.Call(concat, concat_1, Expression.Call(ushort_tostring, current_exp));
                        var concat_3 = Expression.Call(concat, concat_2, Expression.Constant("]"));
                        AddExprs(Expression.Call(write_string, concat_3));
                    }
                    else if (chrs[col] == '>')
                    {
                        Data.TotalChars++;
                        ClearValueCache();
                        if (IsPtrCache)
                        {
                            ptr_cache++;
                            Data.TotalExprs++;
                        } 
                        else Exprs.Add(Expression.AddAssign(pointer_exp, Expression.Constant(1, typeof(int))));
                    }
                    else if (chrs[col] == '<')
                    {
                        Data.TotalChars++;
                        ClearValueCache();
                        if (IsPtrCache)
                        {
                            ptr_cache--;
                            Data.TotalExprs++;
                        }
                        else AddExprs(Expression.SubtractAssign(pointer_exp, Expression.Constant(1, typeof(int))));
                    }
                    else if (chrs[col] == '+')
                    {
                        Data.TotalChars++;
                        ClearPtrCache();
                        if (IsValueCache)
                        {
                            value_cache++;
                            Data.TotalExprs++;
                        } 
                        else
                        {
                            var added = Expression.Add(current_exp, Expression.Constant((ushort)1));
                            AddExprs(Expression.Assign(current_exp, added));
                        }
                    }
                    else if (chrs[col] == '-')
                    {
                        Data.TotalChars++;
                        ClearPtrCache();
                        if (IsValueCache)
                        {
                            value_cache--;
                            Data.TotalExprs++;
                        }
                        else
                        {
                            var subed = Expression.Subtract(current_exp, Expression.Constant((ushort)1));
                            AddExprs(Expression.Assign(current_exp, subed));
                        }
                    }
                    else if (chrs[col] == '.')
                    {
                        Data.TotalChars++;
                        ClearValueCache();
                        ClearPtrCache();
                        AddExprs(Expression.Call(write_char, Expression.Convert(current_exp, typeof(char))));
                    }
                    else if (chrs[col] == ',')
                    {
                        Data.TotalChars++;
                        ClearValueCache();
                        ClearPtrCache();
                        Expression readkey_exp = Expression.Call(readkey, Expression.Constant(true));
                        Expression char_exp = Expression.Property(readkey_exp, typeof(ConsoleKeyInfo).GetProperty("KeyChar"));
                        Expression ushort_exp = Expression.Convert(char_exp, typeof(ushort));
                        AddExprs(Expression.Assign(current_exp, ushort_exp));
                    }
                    else if (chrs[col] == '[')
                    {
                        Data.TotalChars++;
                        ClearValueCache();
                        stack.Push(Exprs.Count); //记录左端的索引
                        depth = stack.Count > depth ? stack.Count : depth;
                        AddExprs(Expression.Empty());//占两个坑
                        AddExprs(Expression.Empty());
                    }
                    else if (chrs[col] == ']')
                    {
                        Data.TotalChars++;
                        ClearValueCache();
                        if (stack.Count == 0) throw new Exception("运算符'['缺失");
                        pair.Add(new Point(stack.Peek(), Exprs.Count)); //记录下索引对，从里到外，从左到右
                        AddExprs(Expression.Empty());   //继续两个占坑
                        AddExprs(Expression.Empty());
                        stack.Pop();
                    }
                    else continue;
                }
            }

            Console.Write("检查跳转栈...");
            if (stack.Count != 0) throw new Exception($"运算符']'缺失");
            Konsole.WriteLnSuccess();

            Console.Write($"导入跳转表，嵌套深度：{depth} ...");
            for (int n = 0; n < pair.Count; n++)
            {
                int left = pair[n].X, right = pair[n].Y;
                //先塞标签进去
                var llbl_exp = Expression.Label($"llbl_{stack.Count()}");
                var rlbl_exp = Expression.Label($"rlbl_{stack.Count()}");
                Exprs[left + 1] = Expression.Label(llbl_exp);
                Exprs[right + 1] = Expression.Label(rlbl_exp);
                //然后整goto
                Exprs[left] = Expression.IfThen(is0_exp, Expression.Goto(rlbl_exp));
                Exprs[right] = Expression.IfThen(is_not0_exp, Expression.Goto(llbl_exp));
            }
            Konsole.WriteLnSuccess();
            if (Pause)
                AddExprs(Expression.Call(typeof(Console).GetMethod("ReadKey", new Type[] { typeof(bool) }), Expression.Constant(true)));
            //return 0;
            AddExprs(Expression.Constant(0));
            sw.Stop();
            Console.WriteLine($"解析完成，共耗时{sw.ElapsedMilliseconds}ms，" +
                $"处理字符{Data.TotalChars}个，其中有效字符{Data.TotalChars}个，原表达式节点{Data.TotalExprs}个，优化后实际表达式节点{Exprs.Count()}个");
        }
        public static void Compile()
        {
            CurrentStatus = Status.COMPILE;
            Console.WriteLine(" >>编译...>>");
            Console.Write($"创建编译模型...");
            var asmBuilder = AssemblyBuilder.DefineDynamicAssembly
                (new AssemblyName(Compiler.OutputName), AssemblyBuilderAccess.RunAndSave);
            var moduleBuilder = asmBuilder.DefineDynamicModule(Compiler.OutputName, Compiler.OutputName);
            Console.Write($"创建主函数...");
            var typeBuilder = moduleBuilder.DefineType("Program", TypeAttributes.Public);
            var methodBuilder = typeBuilder.DefineMethod("Main",
                MethodAttributes.Static, typeof(void), new[] { typeof(string) });
            Console.Write($"导入ExpressionTree...");
            var block = Expression.Block(new ParameterExpression[] { pointer_exp, buffer_exp }, Exprs);
            if (block.CanReduce) block = (BlockExpression)block.ReduceAndCheck();
            Expression.Lambda(block).CompileToMethod(methodBuilder);
            Konsole.WriteLnSuccess();
            Console.Write($"生成类型...");
            typeBuilder.CreateType();
            Console.Write($"设置程序入口点...");
            asmBuilder.SetEntryPoint(methodBuilder);
            Console.Write($"输出结果到文件...");
            asmBuilder.Save(Compiler.OutputName);
            Konsole.WriteLnSuccess();
        }

        static void AddExprs(Expression exp)
        {
            Exprs.Add(exp);
            Data.TotalExprs++;
        }
        static void ClearValueCache()
        {
            if (IsValueCache && value_cache != 0)
            {
                if (value_cache > 0)
                {
                    var added = Expression.Add(current_exp, Expression.Constant((ushort)value_cache));
                    AddExprs(Expression.Assign(current_exp, added));
                    value_cache = 0;
                }
                else
                {
                    int abs = -value_cache;
                    var subed = Expression.Subtract(current_exp, Expression.Constant((ushort)abs));
                    AddExprs(Expression.Assign(current_exp, subed));
                    value_cache = 0;
                }
            }
        }
        static void ClearPtrCache()
        {
            if (IsPtrCache && ptr_cache != 0)
            {
                AddExprs(Expression.AddAssign(pointer_exp, Expression.Constant(ptr_cache, typeof(int))));
                ptr_cache = 0;
            }
        }
        public static class Data
        {
            public static int TotalChars { get; set; } = 0;
            public static int TokenChars { get; set; } = 0;
            public static int TotalExprs { get; set; } = 0;

            public static int Row { get; set; }
            public static int Col { get; set; }
        }
    }

    public static class CompilerException
    {
        public static Exception lackEx = new Exception("参数不足");
        public static Exception srcEx = new Exception("未指定源码文件");
    }

    public enum Status { BUILD, PARSE, COMPILE }
    public enum Optimize { NONE, PTR, VALUE, BOTH }
}
