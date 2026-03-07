using System.Diagnostics;

namespace GitSync;

internal static class ProcessHelper
{
    /// <summary>以隐藏窗口执行命令行</summary>
    /// <param name="fileName">文件名</param>
    /// <param name="arguments">命令参数</param>
    /// <param name="msWait">等待毫秒数</param>
    /// <param name="working">工作目录</param>
    /// <returns>进程退出代码</returns>
    public static Int32 Run(this String fileName, String arguments = null, String working = null, Int32 msWait = 0)
    {
        using var span = DefaultTracer.Instance?.NewSpan("Run", $"{fileName} {arguments} working={working}");
        try
        {
            XTrace.WriteLine("Run {0} {1} {2}", fileName, arguments, msWait);

            var psi = new ProcessStartInfo(fileName, arguments ?? String.Empty)
            {
                //UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = working,
                //RedirectStandardOutput = true,
                //RedirectStandardError = true,
            };

            var process = Process.Start(psi);
            if (process == null) return -1;

            if (msWait == 0) return 0;

            // 如果未退出，则不能拿到退出代码
            if (msWait < 0)
                process.WaitForExit();
            else if (!process.WaitForExit(msWait))
            {
                process.ForceKill();
                return !process.GetHasExited() ? -1 : process.ExitCode;
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            XTrace.Log.Error(ex.Message);

            throw;
        }
    }

    /// <summary>以隐藏窗口执行命令行并捕获标准输出</summary>
    /// <param name="cmd">可执行文件名</param>
    /// <param name="arguments">命令参数</param>
    /// <param name="working">工作目录</param>
    /// <param name="msTimeout">最大等待毫秒数，超时后强制结束进程</param>
    /// <returns>标准输出内容；超时或启动失败返回 null</returns>
    public static String? Execute(this String cmd, String? arguments = null, String? working = null, Int32 msTimeout = 3_000)
    {
        using var span = DefaultTracer.Instance?.NewSpan("Execute", $"{cmd} {arguments} working={working}");
        try
        {
            XTrace.WriteLine("{0} {1}", cmd, arguments);

            var psi = new ProcessStartInfo(cmd, arguments ?? String.Empty)
            {
                // UseShellExecute 必须 false，以便于后续重定向输出流
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = working,
                RedirectStandardOutput = true,
                //RedirectStandardError = true,
            };
            var process = Process.Start(psi);
            if (process == null) return null;

            if (!process.WaitForExit(msTimeout))
            {
                //process.Kill(true);
                process.ForceKill();
                return null;
            }

            return process.StandardOutput.ReadToEnd();
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            XTrace.Log.Error(ex.Message);
            return null;
        }
    }

    /// <summary>以隐藏窗口执行命令行，将标准输出和标准错误异步转发到 XTrace 日志</summary>
    /// <param name="cmd">可执行文件名</param>
    /// <param name="arguments">命令参数</param>
    /// <param name="working">工作目录</param>
    /// <param name="msTimeout">最大等待毫秒数，超时后强制结束进程</param>
    /// <returns>进程退出代码；超时返回进程最新退出代码，启动失败返回 -2</returns>
    public static Int32 ShellExecute(this String cmd, String? arguments = null, String? working = null, Int32 msTimeout = 30_000)
    {
        using var span = DefaultTracer.Instance?.NewSpan("ShellExecute", $"{cmd} {arguments} working={working}");
        try
        {
            XTrace.WriteLine("{0} {1}", cmd, arguments);

            var psi = new ProcessStartInfo(cmd, arguments ?? String.Empty)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                //WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = working,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) => XTrace.WriteLine(e.Data);
            process.ErrorDataReceived += (s, e) => XTrace.WriteLine(e.Data);
            process.Start();
            //if (process == null) return -1;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(msTimeout))
            {
                process.ForceKill();
                return process.ExitCode;
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);
            XTrace.Log.Error(ex.Message);
            return -2;
        }
    }
}
