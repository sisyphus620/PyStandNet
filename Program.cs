//#define PYSTAND_CONSOLE
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

PyStand ps = new PyStand("runtime");
if (ps.DetectScript() != 0)
{
    return 3;
}
#if PYSTAND_CONSOLE
const int ATTACH_PARENT_PROCESS = -1;
if (InvokeHelper.AttachConsole(ATTACH_PARENT_PROCESS))
{
    InvokeHelper.RedirectStandardOutput();
    InvokeHelper.RedirectStandardError();
    InvokeHelper.RedirectStdioFileDescriptors(stdout: Console.OpenStandardOutput());
    InvokeHelper.RedirectStdioFileDescriptors(stderr: Console.OpenStandardError());
    int stdoutFd = InvokeHelper.GetFileDescriptor(Console.OpenStandardOutput());
    if (stdoutFd >= 0)
    {
        string fn = stdoutFd.ToString();
        InvokeHelper.SetEnvironmentVariable("PYSTAND_STDOUT", fn);
    }
    int stdinFd = InvokeHelper.GetFileDescriptor(Console.OpenStandardInput());
    if (stdinFd >= 0)
    {
        string fn = stdinFd.ToString();
        InvokeHelper.SetEnvironmentVariable("PYSTAND_STDIN", fn);
    }
}
#endif
int hr = ps.RunString(PyStand.init_script);
// printf("finalize\n");
return hr;


class PyStand : IDisposable
{
    private bool disposedValue;
    public delegate int t_Py_Main(int argc, IntPtr argv);
    public delegate void MyLog(string? msg);
    protected MyLog myLog = Console.WriteLine;
    protected t_Py_Main? _Py_Main;
    protected IntPtr _hDLL;
    protected string _cwd = string.Empty;      // current working directory
    protected string _args = string.Empty;     // arguments
    protected string? _pystand;                // absolute path of pystand
    protected string _runtime = string.Empty;  // absolute path of embedded python runtime
    protected string _home = string.Empty;     // home directory of PyStand.exe
    protected string _script = string.Empty;   // init script like PyStand.int or PyStand.py
    protected List<string> _argv = new List<string>();
    protected List<string> _py_argv = new List<string>();
    protected List<string> _py_args = new List<string>();

    public PyStand(char[] runtime)
    {
        _hDLL = IntPtr.Zero;
        _Py_Main = null;
        if (CheckEnviron(runtime.ToString()) == false)
        {
            Environment.Exit(1);
        }
        if (LoadPython() == false)
        {
            Environment.Exit(2);
        }
    }
    public PyStand(string runtime)
    {
        _hDLL = IntPtr.Zero;
        _Py_Main = null;
        if (CheckEnviron(runtime) == false)
        {
            Environment.Exit(1);
        }
        if (LoadPython() == false)
        {
            Environment.Exit(2);
        }
    }
    public string Ansi2Unicode(char[] text)
    {
        return Encoding.ASCII.GetString(Array.ConvertAll(text, Convert.ToByte));
    }
    public int RunString(string script)
    {
        if (_Py_Main == null)
        {
            return -1;
        }
        int hr = 0;
        int i;
        _py_argv.Clear();
        // init arguments
        _py_argv.Add(_argv[0]);
        _py_argv.Add("-I");
        _py_argv.Add("-s");
        _py_argv.Add("-S");
        _py_argv.Add("-c");
        _py_argv.Add(script);
        for (i = 1; i < (int)_argv.Count; i++)
        {
            _py_argv.Add(_argv[i]);
        }
        // finalize arguments
        _py_args.Clear();
        for (i = 0; i < (int)_py_argv.Count; i++)
        {
            _py_args.Add(_py_argv[i]);
        }
        string[] _py_args_arr = _py_args.ToArray();
        IntPtr argPtr = IntPtr.Zero;
        // unsafe { fixed (string* ptr = _py_args_arr) { argPtr = (IntPtr)ptr; } } // func 1, failed, 0
        // unsafe { argPtr = (IntPtr)_py_args_arr.AsMemory().Pin().Pointer; } // func 2, error, -1
        // correct usage
        int size = IntPtr.Size * _py_args_arr.Length;
        argPtr = Marshal.AllocHGlobal(size);
        for (int ii = 0; ii < _py_args_arr.Length; ii++)
        {
            IntPtr tmpPtr = Marshal.StringToHGlobalUni(_py_args_arr[ii]);
            Marshal.WriteIntPtr(argPtr, ii * IntPtr.Size, tmpPtr);
        }
        // error: PYSTAND,, has not been inject into environ success
        Console.WriteLine(Process.GetCurrentProcess().Id);
        hr = _Py_Main(_py_args_arr.Length, argPtr);
        return hr;
    }
    public int RunString(char[] script)
    {
        if (string.IsNullOrEmpty(script.ToString())) return -1;
        else return RunString(script.ToString());
    }
    public int DetectScript()
    {
        // init: _script (init script like PyStand.int or PyStand.py)
        if (_pystand == null) return -1;
        int size = _pystand.Length - 1;
        for (; size >= 0; size--)
        {
            if (_pystand[size] == '.') break;
        }
	    if (size< 0) size = _pystand.Length;
        string main = _pystand.Substring(0, size);
        List<string> exts = new List<string>();
        List<string> scripts = new List<string>();
        _script = string.Empty;
        if(_script == string.Empty)
        {
            exts.Add(".int");
            exts.Add(".py");
            exts.Add(".pyw");
            for (int i = 0; i < exts.Count; i++)
            {
                string test = main + exts[i];
                scripts.Add(test);
                if (System.IO.File.Exists(test))
                {
                    _script = test;
                    break;
                }
            }
            if (_script.Length == 0)
            {
                myLog("Cannot find " + scripts);
                return -1;
            }
        }
        Environment.SetEnvironmentVariable("PYSTAND_SCRIPT", _script);
        return 0;
    }
    // protected
    protected bool LoadPython()
    {
        string runtime = _runtime;
        string previous = Environment.CurrentDirectory;
        Directory.SetCurrentDirectory(runtime);
        _hDLL = NativeLibrary.Load("python3.dll");
        if (_hDLL != IntPtr.Zero) _Py_Main = Marshal.GetDelegateForFunctionPointer<t_Py_Main>(
            NativeLibrary.GetExport(_hDLL, "Py_Main"));
        Directory.SetCurrentDirectory(previous);
        if (_hDLL == IntPtr.Zero)
        {
            myLog("ERROR: dll not found");
            return false;
        }
        else if(_Py_Main == null)
        {
            myLog("ERROR: Py_Main() not found");
            return false;
        }
        return true;
    }
    private bool CheckEnviron(string? rtp)
    {
        // Environment.CommandLine = "xx/PyStandNet.dll", maybe useful with aot, but here replace it
        // with "Process.GetCurrentProcess().MainModule"
        if (Process.GetCurrentProcess().MainModule == null) return false;
        else
        {
            _args = Process.GetCurrentProcess().MainModule.FileName;
            _pystand = Process.GetCurrentProcess().MainModule.FileName;
        }
        _argv = Environment.GetCommandLineArgs().ToList();
        if (_args == string.Empty)
        {
            myLog("ERROR in Environment.CommandLine");
            return false;
        }
        if (_argv.Count > 0) _argv[0] = _args;
        _cwd = Environment.CurrentDirectory;
        _home = Environment.CurrentDirectory;
        if (Path.IsPathRooted(rtp))
        {
            _runtime = rtp;
        }
        else if (rtp != null)
        {
            _runtime = Path.Combine(_home, rtp);
        }
        else _runtime = _home;
        if (!Path.Exists(_runtime))
        {
            myLog("ERROR in runtime path");
            return false;
        }
        if (!System.IO.File.Exists(_runtime + "\\python3.dll"))
        {
            myLog("ERROR in runtime file");
            return false;
        }
        if (_pystand == null)
        {
            myLog("ERROR in finding init_script.int");
            return false;
        }
        Environment.SetEnvironmentVariable("PYSTAND", _pystand);
        Environment.SetEnvironmentVariable("PYSTAND_HOME", _home);
        Environment.SetEnvironmentVariable("PYSTAND_RUNTIME", _runtime);
        return true;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // TODO: 释放托管状态(托管对象)
                Marshal.FreeHGlobal(_hDLL);
            }
            disposedValue = true;
        }
    }
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    //---------------------------------------------------------------------
    // init script
    //---------------------------------------------------------------------

#if PYSTAND_CONSOLE
    public static string init_script =
        @"import sys
import os
import copy
import site
PYSTAND = os.environ['PYSTAND']
PYSTAND_HOME = os.environ['PYSTAND_HOME']
PYSTAND_RUNTIME = os.environ['PYSTAND_RUNTIME']
PYSTAND_SCRIPT = os.environ['PYSTAND_SCRIPT']
sys.path_origin = [n for n in sys.path]
sys.PYSTAND = PYSTAND
sys.PYSTAND_HOME = PYSTAND_HOME
sys.PYSTAND_SCRIPT = PYSTAND_SCRIPT
def MessageBox(msg, info = 'Message'):
    import ctypes
    ctypes.windll.user32.MessageBoxW(None, str(msg), str(info), 0)
    return 0
os.MessageBox = MessageBox
try:
    fd = os.open('CONOUT$', os.O_RDWR | os.O_BINARY)
    fp = os.fdopen(fd, 'w')
    sys.stdout = fp
    sys.stderr = fp
except Exception as e:
    fp = open(os.devnull, 'w')
    sys.stdout = fp
    sys.stderr = fp
for n in ['.', 'lib', 'site-packages']:
    test = os.path.abspath(os.path.join(PYSTAND_HOME, n))
    if os.path.exists(test):
        site.addsitedir(test)
sys.argv = [PYSTAND_SCRIPT] + sys.argv[1:]
text = open(PYSTAND_SCRIPT, 'rb').read()
environ = {'__file__': PYSTAND_SCRIPT, '__name__': '__main__'}
environ['__package__'] = None
try:
    code = compile(text, PYSTAND_SCRIPT, 'exec')
    exec(code, environ)
except:
    import traceback, io
    sio = io.StringIO()
    traceback.print_exc(file = sio)
    os.MessageBox(sio.getvalue(), 'Error')
";
#else
    public static string init_script =
@"import sys
import os
import copy
import site
print('pid:', os.getpid())
print('ppid:', os.getppid())
PYSTAND = os.environ['PYSTAND']
PYSTAND_HOME = os.environ['PYSTAND_HOME']
PYSTAND_RUNTIME = os.environ['PYSTAND_RUNTIME']
PYSTAND_SCRIPT = os.environ['PYSTAND_SCRIPT']
sys.path_origin = [n for n in sys.path]
sys.PYSTAND = PYSTAND
sys.PYSTAND_HOME = PYSTAND_HOME
sys.PYSTAND_SCRIPT = PYSTAND_SCRIPT
def MessageBox(msg, info = 'Message'):
    import ctypes
    ctypes.windll.user32.MessageBoxW(None, str(msg), str(info), 0)
    return 0
os.MessageBox = MessageBox
try:
    fd = os.open('CONOUT$', os.O_RDWR | os.O_BINARY)
    fp = os.fdopen(fd, 'w')
    sys.stdout = fp
    sys.stderr = fp
except Exception as e:
    fp = open(os.devnull, 'w')
    sys.stdout = fp
    sys.stderr = fp
for n in ['.', 'lib', 'site-packages']:
    test = os.path.abspath(os.path.join(PYSTAND_HOME, n))
    if os.path.exists(test):
        site.addsitedir(test)
sys.argv = [PYSTAND_SCRIPT] + sys.argv[1:]
text = open(PYSTAND_SCRIPT, 'rb').read()
environ = {'__file__': PYSTAND_SCRIPT, '__name__': '__main__'}
environ['__package__'] = None
code = compile(text, PYSTAND_SCRIPT, 'exec')
exec(code, environ)
";
#endif
}

static class InvokeHelper
{
    public static bool AttachConsole(int dwProcessId)
    {
        try
        {
            IntPtr consoleWndHandle = GetConsoleWindowHandle();
            return consoleWndHandle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }
    public  static void RedirectStandardOutput()
    {
        StreamWriter stdOutWriter = new StreamWriter(Console.OpenStandardOutput());
        stdOutWriter.AutoFlush = true;
        Console.SetOut(stdOutWriter);
    }
    public  static void RedirectStandardError()
    {
        StreamWriter stdErrWriter = new StreamWriter(Console.OpenStandardError());
        stdErrWriter.AutoFlush = true;
        Console.SetError(stdErrWriter);
    }
    public  static void RedirectStdioFileDescriptors(Stream stdout = null, Stream stderr = null)
    {
        if (stdout != null)
            Console.SetOut(new StreamWriter(stdout));

        if (stderr != null)
            Console.SetError(new StreamWriter(stderr));
    }
    public  static int GetFileDescriptor(Stream stream)
    {
        if (stream is FileStream fileStream)
        {
            try
            {
                return fileStream.SafeFileHandle.DangerousGetHandle().ToInt32();
            }
            catch
            {
                return -1;
            }
        }

        return -1;
    }
    public  static void SetEnvironmentVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
    }
    public  static IntPtr GetConsoleWindowHandle()
    {
        return NativeMethods.GetConsoleWindow();
    }
    public  static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetConsoleWindow();
    }

    // Windows API
    [DllImport("kernel32.dll")]
    private static extern IntPtr LoadLibrary(string dllToLoad);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr hModule);
}

static class PyHelper
{
    [DllImport("python3.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr Py_Initialize();

    [DllImport("python3.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Py_Finalize();

    [DllImport("python3.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PyRun_SimpleString(string command);
}