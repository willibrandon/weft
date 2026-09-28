#!/usr/bin/env -S dotnet --
#:property AllowUnsafeBlocks=true

using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Weft.Tests.Programs;

/// <summary>
/// A small, deterministic shell that tests run in real pseudo-terminals on every operating system.
/// </summary>
/// <remarks>
/// It prints a <c>$ </c> prompt and reads one line at a time. <c>-c</c> runs a script and exits.
/// Commands are separated by <c>;</c>; single quotes keep text literal, and <c>$((a*b))</c>,
/// <c>$((a+b))</c>, and <c>$((a-b))</c> expand outside single quotes. The commands are
/// <c>echo</c>, <c>print</c> (printf escapes, no format), <c>repeat first last format</c> (<c>%d</c>
/// or <c>%03d</c> receive the number), <c>sleep seconds</c>, <c>read</c>, <c>noecho</c>, <c>raw</c>,
/// <c>cooked</c>, <c>bytes count</c> (prints received byte values), <c>record count file</c>,
/// <c>touch file</c>, <c>cat file</c>, and <c>exit [code]</c>.
/// </remarks>
internal static partial class TestShell
{
    private const int StandardInput = -10;
    private const int StandardOutput = -11;
    private const uint EchoInput = 0x0004;
    private const uint VirtualTerminalProcessing = 0x0004;
    private const uint VirtualTerminalInput = 0x0200;
    private static readonly List<byte> s_pending = [];
    private static Stream s_input = Stream.Null;
    private static Stream s_output = Stream.Null;
    private static uint s_inputMode;
    private static SafeFileHandle? s_inputHandle;
    private static SafeFileHandle? s_outputHandle;

    private static int Main(string[] args)
    {
        OpenTerminal();
        try
        {
            if (args is ["-c", string script])
            {
                return Run(script) ?? 0;
            }

            while (true)
            {
                Write("$ ");
                string? line = ReadLine();
                if (line is null)
                {
                    return 0;
                }

                if (Run(line) is int code)
                {
                    return code;
                }
            }
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int? Run(string script)
    {
        foreach (List<string> command in Parse(script).Where(words => words.Count != 0))
        {
            string[] arguments = [.. command.Skip(1)];
            switch (command[0])
            {
                case "echo":
                    Write(string.Join(' ', arguments) + "\n");
                    break;
                case "print":
                    Write(string.Concat(arguments.Select(Unescape)));
                    break;
                case "repeat" when arguments.Length == 3:
                    var text = new StringBuilder();
                    for (int number = Number(arguments[0]); number <= Number(arguments[1]); number++)
                    {
                        _ = text.Append(Format(Unescape(arguments[2]), number));
                    }

                    Write(text.ToString());
                    break;
                case "sleep" when arguments.Length == 1:
                    Thread.Sleep(TimeSpan.FromSeconds(double.Parse(arguments[0], CultureInfo.InvariantCulture)));
                    break;
                case "read":
                    _ = ReadLine();
                    break;
                case "noecho":
                case "raw":
                case "cooked":
                    SetMode(command[0]);
                    break;
                case "bytes" when arguments.Length == 1:
                    Write(string.Join(' ', ReadBytes(Number(arguments[0])).Select(value => value.ToString(CultureInfo.InvariantCulture))) + "\n");
                    break;
                case "record" when arguments.Length == 2:
                    Record(Number(arguments[0]), arguments[1]);
                    break;
                case "touch" when arguments.Length == 1:
                    File.WriteAllBytes(arguments[0], []);
                    break;
                case "cat" when arguments.Length == 1:
                    using (FileStream file = File.OpenRead(arguments[0]))
                    {
                        file.CopyTo(s_output);
                    }

                    break;
                case "exit":
                    return arguments.Length == 0 ? 0 : Number(arguments[0]);
                default:
                    Write("test shell: unknown command " + command[0] + "\n");
                    break;
            }
        }

        return null;
    }

    private static IEnumerable<List<string>> Parse(string script)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        bool quoted = false;
        bool pending = false;
        for (int index = 0; index < script.Length; index++)
        {
            char character = script[index];
            if (quoted)
            {
                if (character == '\'')
                {
                    quoted = false;
                }
                else
                {
                    _ = word.Append(character);
                }
            }
            else if (character == '\'')
            {
                quoted = true;
                pending = true;
            }
            else if (character is ' ' or '\t' or ';' or '\r' or '\n')
            {
                if (pending || word.Length != 0)
                {
                    words.Add(word.ToString());
                    _ = word.Clear();
                    pending = false;
                }

                if (character == ';')
                {
                    yield return words;
                    words = [];
                }
            }
            else if (character == '$' && script.AsSpan(index).StartsWith("$((") && script.IndexOf("))", index, StringComparison.Ordinal) is int end and > 0)
            {
                _ = word.Append(Evaluate(script[(index + 3)..end]).ToString(CultureInfo.InvariantCulture));
                index = end + 1;
            }
            else
            {
                _ = word.Append(character);
            }
        }

        if (pending || word.Length != 0)
        {
            words.Add(word.ToString());
        }

        yield return words;
    }

    private static long Evaluate(string expression)
    {
        int operation = expression.IndexOfAny(['*', '+', '-'], 1);
        if (operation < 0)
        {
            return Number(expression.Trim());
        }

        long left = Number(expression[..operation].Trim());
        long right = Number(expression[(operation + 1)..].Trim());
        return expression[operation] switch
        {
            '*' => left * right,
            '+' => left + right,
            _ => left - right
        };
    }

    private static int Number(string text)
    {
        return int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static string Format(string format, int number)
    {
        var text = new StringBuilder();
        int index = 0;
        while (index < format.Length)
        {
            int end = index + 1;
            while (end < format.Length && char.IsAsciiDigit(format[end]))
            {
                end++;
            }

            if (format[index] == '%' && end < format.Length && format[end] == 'd')
            {
                int width = end == index + 1 ? 0 : int.Parse(format.AsSpan(index + 1, end - index - 1), CultureInfo.InvariantCulture);
                _ = text.Append(number.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'));
                index = end + 1;
            }
            else
            {
                _ = text.Append(format[index]);
                index++;
            }
        }

        return text.ToString();
    }

    private static string Unescape(string text)
    {
        var result = new StringBuilder();
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != '\\' || index + 1 >= text.Length)
            {
                _ = result.Append(text[index]);
                continue;
            }

            char next = text[++index];
            switch (next)
            {
                case 'n':
                    _ = result.Append('\n');
                    break;
                case 'r':
                    _ = result.Append('\r');
                    break;
                case 't':
                    _ = result.Append('\t');
                    break;
                case 'a':
                    _ = result.Append('\a');
                    break;
                case 'e':
                    _ = result.Append('\u001b');
                    break;
                case 'x':
                    int hexLength = 0;
                    while (hexLength < 2 && index + 1 + hexLength < text.Length && char.IsAsciiHexDigit(text[index + 1 + hexLength]))
                    {
                        hexLength++;
                    }

                    _ = result.Append((char)int.Parse(text.AsSpan(index + 1, hexLength), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    index += hexLength;
                    break;
                case >= '0' and <= '7':
                    // As in a printf format, up to three digits including any leading zero.
                    int length = 1;
                    while (length < 3 && index + length < text.Length && text[index + length] is >= '0' and <= '7')
                    {
                        length++;
                    }

                    _ = result.Append((char)Convert.ToInt32(text.Substring(index, length), 8));
                    index += length - 1;
                    break;
                default:
                    _ = result.Append(next);
                    break;
            }
        }

        return result.ToString();
    }

    private static void Write(string text)
    {
        s_output.Write(Encoding.UTF8.GetBytes(text));
        s_output.Flush();
    }

    private static int ReadByte()
    {
        if (s_pending.Count != 0)
        {
            byte value = s_pending[0];
            s_pending.RemoveAt(0);
            return value;
        }

        Span<byte> buffer = stackalloc byte[256];
        int count = s_input.Read(buffer);
        if (count <= 0)
        {
            return -1;
        }

        s_pending.AddRange(buffer[1..count]);
        return buffer[0];
    }

    private static string? ReadLine()
    {
        var line = new List<byte>();
        while (true)
        {
            int value = ReadByte();
            if (value < 0)
            {
                return line.Count == 0 ? null : Encoding.UTF8.GetString([.. line]);
            }

            if (value == '\n')
            {
                return Encoding.UTF8.GetString([.. line]).TrimEnd('\r');
            }

            line.Add((byte)value);
        }
    }

    private static byte[] ReadBytes(int count)
    {
        byte[] bytes = new byte[count];
        for (int index = 0; index < count; index++)
        {
            int value = ReadByte();
            if (value < 0)
            {
                return bytes[..index];
            }

            bytes[index] = (byte)value;
        }

        return bytes;
    }

    private static void Record(int count, string path)
    {
        using FileStream file = File.Create(path);
        for (int index = 0; index < count; index++)
        {
            int value = ReadByte();
            if (value < 0)
            {
                return;
            }

            // Observers watch the file grow one received byte at a time.
            file.WriteByte((byte)value);
            file.Flush();
        }
    }

    private static void OpenTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The console layer would take over echo and line editing; the terminal driver keeps them here.
            s_inputHandle = new SafeFileHandle(0, ownsHandle: false);
            s_outputHandle = new SafeFileHandle(1, ownsHandle: false);
            s_input = new FileStream(s_inputHandle, FileAccess.Read, 0);
            s_output = new FileStream(s_outputHandle, FileAccess.Write, 0);
            return;
        }

        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        nint output = GetStdHandle(StandardOutput);
        if (GetConsoleMode(output, out uint outputMode))
        {
            _ = SetConsoleMode(output, outputMode | VirtualTerminalProcessing);
        }

        _ = GetConsoleMode(GetStdHandle(StandardInput), out s_inputMode);
        s_input = Console.OpenStandardInput();
        s_output = Console.OpenStandardOutput();
    }

    private static void SetMode(string mode)
    {
        if (OperatingSystem.IsWindows())
        {
            uint value = mode switch
            {
                "raw" => VirtualTerminalInput,
                "noecho" => s_inputMode & ~EchoInput,
                _ => s_inputMode
            };
            _ = SetConsoleMode(GetStdHandle(StandardInput), value);
            return;
        }

        string[] arguments = mode switch
        {
            "raw" => ["raw", "-echo"],
            "noecho" => ["-echo"],
            _ => ["sane"]
        };
        using Process process = Process.Start(new ProcessStartInfo("stty", arguments) { UseShellExecute = false })
            ?? throw new IOException("stty could not be started.");
        process.WaitForExit();
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);
}
