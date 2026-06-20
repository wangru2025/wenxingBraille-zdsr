using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ZDSR.BrailleDisplayAddin;

namespace WenxingBrailleZdsrAddin
{
    public sealed class WenxingBrailleDisplay : IBrailleDisplayAddin
    {
        private const int CellCount = 40;
        private const int IdleKey = 255;
        private const int PollIntervalMilliseconds = 50;

        private readonly object nativeSync = new object();
        private readonly object keyMapSync = new object();
        private Action<int, int> setRowCellHandle;
        private Action<string> actionHandler;
        private Action<int> routingKeyHandler;
        private CancellationTokenSource keyPollingCts;
        private Task keyPollingTask;
        private Dictionary<int, string> keyMap = new Dictionary<int, string>();
        private DateTime keyMapWriteTimeUtc = DateTime.MinValue;
        private string pluginDirectory;
        private bool connected;

        public string Name
        {
            get { return "文星点显器"; }
        }

        public string Description
        {
            get { return "文星点显器 WinUSB 驱动"; }
        }

        public int GetVersion()
        {
            return 1;
        }

        public bool Initial(Action<int, int> setRowCell, Action<string> actionHandler, Action<int> routingKeyHandler)
        {
            setRowCellHandle = setRowCell;
            this.actionHandler = actionHandler;
            this.routingKeyHandler = routingKeyHandler;
            pluginDirectory = Path.GetDirectoryName(typeof(WenxingBrailleDisplay).Assembly.Location);

            try
            {
                NativeMethods.EnsureLoaded();
                EnsureDefaultKeyMap();
                LoadKeyMapIfNeeded();
                return true;
            }
            catch (Exception ex)
            {
                Log("Initial failed: " + ex);
                return false;
            }
        }

        public Task<bool> ConnectAsync()
        {
            return Task.Run(
                delegate
                {
                    try
                    {
                        int result;
                        lock (nativeSync)
                        {
                            result = NativeMethods.OpenBrailleDisplay();
                        }

                        connected = result == 1;
                        if (!connected)
                        {
                            Log("Connect failed: " + NativeMethods.LastError);
                            return false;
                        }

                        if (setRowCellHandle != null)
                        {
                            try
                            {
                                setRowCellHandle(1, CellCount);
                            }
                            catch (Exception ex)
                            {
                                Log("setRowCellHandle failed: " + ex);
                            }
                        }

                        StartKeyPolling();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        connected = false;
                        try
                        {
                            StopKeyPolling();
                            lock (nativeSync)
                            {
                                NativeMethods.CloseBrailleDisplay();
                            }
                        }
                        catch
                        {
                        }

                        Log("Connect exception: " + ex);
                        return false;
                    }
                });
        }

        public void Disconnect()
        {
            try
            {
                if (!connected)
                {
                    return;
                }

                StopKeyPolling();

                try
                {
                    lock (nativeSync)
                    {
                        NativeMethods.CloseBrailleDisplay();
                    }
                }
                finally
                {
                    connected = false;
                }
            }
            catch (Exception ex)
            {
                connected = false;
                Log("Disconnect exception: " + ex);
            }
        }

        public void WriteCells(byte[] cells)
        {
            try
            {
                if (!connected || cells == null)
                {
                    return;
                }

                byte[] output = new byte[CellCount];
                Buffer.BlockCopy(cells, 0, output, 0, Math.Min(cells.Length, output.Length));
                int result;
                lock (nativeSync)
                {
                    result = NativeMethods.ShowBraille(output);
                }

                if (result != 1)
                {
                    Log("ShowBraille failed: " + NativeMethods.LastError);
                }
            }
            catch (Exception ex)
            {
                Log("WriteCells exception: " + ex);
            }
        }

        private void StartKeyPolling()
        {
            StopKeyPolling();

            keyPollingCts = new CancellationTokenSource();
            CancellationToken token = keyPollingCts.Token;
            keyPollingTask = Task.Factory.StartNew(
                delegate { PollKeys(token); },
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        private void StopKeyPolling()
        {
            CancellationTokenSource cts = keyPollingCts;
            Task task = keyPollingTask;
            keyPollingCts = null;
            keyPollingTask = null;

            if (cts == null)
            {
                return;
            }

            cts.Cancel();
            try
            {
                if (task != null)
                {
                    task.Wait(500);
                }
            }
            catch (AggregateException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        private void PollKeys(CancellationToken token)
        {
            int lastKey = IdleKey;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    int key = IdleKey;
                    lock (nativeSync)
                    {
                        if (connected)
                        {
                            key = NativeMethods.GetBtn();
                        }
                    }

                    if (key == IdleKey)
                    {
                        lastKey = IdleKey;
                    }
                    else if (key <= 0)
                    {
                        if (key != lastKey)
                        {
                            Log("GetBtn ignored: " + key.ToString(CultureInfo.InvariantCulture) + " " + NativeMethods.LastError);
                        }
                        lastKey = key;
                    }
                    else if (key >= 0 && key != lastKey)
                    {
                        lastKey = key;
                        HandleKey(key);
                    }
                }
                catch (Exception ex)
                {
                    Log("PollKeys failed: " + ex);
                }

                token.WaitHandle.WaitOne(PollIntervalMilliseconds);
            }
        }

        private void HandleKey(int key)
        {
            LoadKeyMapIfNeeded();

            string command = null;
            lock (keyMapSync)
            {
                keyMap.TryGetValue(key, out command);
            }

            if (String.IsNullOrWhiteSpace(command))
            {
                Log("Unknown key: " + key.ToString(CultureInfo.InvariantCulture));
                return;
            }

            try
            {
                if (command.StartsWith("routing:", StringComparison.OrdinalIgnoreCase))
                {
                    int cell;
                    string value = command.Substring("routing:".Length).Trim();
                    if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out cell))
                    {
                        if (routingKeyHandler != null)
                        {
                            routingKeyHandler(cell);
                        }
                        Log("Key " + key.ToString(CultureInfo.InvariantCulture) + " -> routing:" + cell.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        Log("Invalid routing mapping for key " + key.ToString(CultureInfo.InvariantCulture) + ": " + command);
                    }
                    return;
                }

                if (actionHandler != null)
                {
                    actionHandler(command);
                }
                Log("Key " + key.ToString(CultureInfo.InvariantCulture) + " -> " + command);
            }
            catch (Exception ex)
            {
                Log("HandleKey failed for key " + key.ToString(CultureInfo.InvariantCulture) + ": " + ex);
            }
        }

        private void EnsureDefaultKeyMap()
        {
            string path = GetKeyMapPath();
            if (File.Exists(path))
            {
                return;
            }

            File.WriteAllText(path, DefaultKeyMapText);
        }

        private void LoadKeyMapIfNeeded()
        {
            string path = GetKeyMapPath();
            if (!File.Exists(path))
            {
                return;
            }

            DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
            lock (keyMapSync)
            {
                if (writeTimeUtc == keyMapWriteTimeUtc)
                {
                    return;
                }
            }

            Dictionary<int, string> loaded = new Dictionary<int, string>();
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                int key;
                string keyText = line.Substring(0, equals).Trim();
                string command = line.Substring(equals + 1).Trim();
                if (command.Length == 0)
                {
                    continue;
                }

                if (Int32.TryParse(keyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out key))
                {
                    loaded[key] = command;
                }
            }

            lock (keyMapSync)
            {
                keyMap = loaded;
                keyMapWriteTimeUtc = writeTimeUtc;
            }
            Log("Loaded KeyMap.ini, entries: " + loaded.Count.ToString(CultureInfo.InvariantCulture));
        }

        private string GetKeyMapPath()
        {
            return Path.Combine(GetPluginDirectory(), "KeyMap.ini");
        }

        private string GetLogPath()
        {
            return Path.Combine(GetPluginDirectory(), "keylog.txt");
        }

        private string GetPluginDirectory()
        {
            if (!String.IsNullOrEmpty(pluginDirectory))
            {
                return pluginDirectory;
            }

            pluginDirectory = Path.GetDirectoryName(typeof(WenxingBrailleDisplay).Assembly.Location);
            return pluginDirectory;
        }

        private void Log(string message)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine;
                File.AppendAllText(GetLogPath(), line);
            }
            catch
            {
            }
        }

        private const string DefaultKeyMapText =
@"# 文星点显器按键映射。格式：键码=争渡动作名
# 修改本文件后不需要重启插件，下一次按键会自动重载。
# GetBtn 返回值：-1 通讯失败，1..40 路由键，41..43 左侧外到内，44..46 右侧外到内，255 空闲。
1=routing:1
2=routing:2
3=routing:3
4=routing:4
5=routing:5
6=routing:6
7=routing:7
8=routing:8
9=routing:9
10=routing:10
11=routing:11
12=routing:12
13=routing:13
14=routing:14
15=routing:15
16=routing:16
17=routing:17
18=routing:18
19=routing:19
20=routing:20
21=routing:21
22=routing:22
23=routing:23
24=routing:24
25=routing:25
26=routing:26
27=routing:27
28=routing:28
29=routing:29
30=routing:30
31=routing:31
32=routing:32
33=routing:33
34=routing:34
35=routing:35
36=routing:36
37=routing:37
38=routing:38
39=routing:39
40=routing:40

# 左侧三个键：外、中、内；右侧三个键：外、中、内。
# 42/45 来自 Sunshine 映射：上一屏/下一屏。
41=br_PreviousLine
42=br_PreviousScreen
43=br_FirstScreen
44=br_NextLine
45=br_NextScreen
46=br_LastScreen
";
    }

    internal static class NativeMethods
    {
        private static readonly Guid DeviceInterfaceGuid = new Guid("58D07210-27C1-11DD-BD0B-0800200C9A66");
        private const byte OutPipeId = 0x01;
        private const byte InPipeId = 0x81;
        private const int PacketSize = 64;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint FileFlagOverlapped = 0x40000000;
        private const int InvalidHandleValue = -1;
        private const int ErrorNoMoreItems = 259;
        private const int DigcfPresent = 0x00000002;
        private const int DigcfDeviceinterface = 0x00000010;

        private static IntPtr deviceHandle = IntPtr.Zero;
        private static IntPtr winUsbHandle = IntPtr.Zero;
        private static string lastError = String.Empty;

        internal static string LastError
        {
            get { return String.IsNullOrEmpty(lastError) ? "No error detail" : lastError; }
        }

        internal static int OpenBrailleDisplay()
        {
            try
            {
                lastError = String.Empty;
                CloseBrailleDisplay();

                string devicePath = FindDevicePath();
                if (String.IsNullOrEmpty(devicePath))
                {
                    SetLastError("Device interface not found");
                    return 0;
                }

                deviceHandle = CreateFile(
                    devicePath,
                    GenericRead | GenericWrite,
                    FileShareRead | FileShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    FileAttributeNormal | FileFlagOverlapped,
                    IntPtr.Zero);

                if (IsInvalidHandle(deviceHandle))
                {
                    SetLastError("CreateFile failed", Marshal.GetLastWin32Error());
                    deviceHandle = IntPtr.Zero;
                    return 0;
                }

                if (!WinUsb_Initialize(deviceHandle, out winUsbHandle))
                {
                    SetLastError("WinUsb_Initialize failed", Marshal.GetLastWin32Error());
                    CloseBrailleDisplay();
                    return 0;
                }

                return 1;
            }
            catch (Exception ex)
            {
                SetLastError("Open exception: " + ex.Message);
                try
                {
                    CloseBrailleDisplay();
                }
                catch
                {
                }
                return 0;
            }
        }

        internal static int CloseBrailleDisplay()
        {
            try
            {
                if (winUsbHandle != IntPtr.Zero)
                {
                    WinUsb_Free(winUsbHandle);
                    winUsbHandle = IntPtr.Zero;
                }

                if (deviceHandle != IntPtr.Zero)
                {
                    CloseHandle(deviceHandle);
                    deviceHandle = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                SetLastError("Close exception: " + ex.Message);
                deviceHandle = IntPtr.Zero;
                winUsbHandle = IntPtr.Zero;
            }

            return 1;
        }

        internal static int ShowBraille(byte[] cells)
        {
            try
            {
                if (winUsbHandle == IntPtr.Zero || cells == null)
                {
                    SetLastError("ShowBraille called while disconnected");
                    return 0;
                }

                byte[] packet = new byte[PacketSize];
                packet[0] = 0x80;
                Buffer.BlockCopy(cells, 0, packet, 1, Math.Min(cells.Length, 40));

                uint transferred;
                bool ok = WinUsb_WritePipe(winUsbHandle, OutPipeId, packet, (uint)packet.Length, out transferred, IntPtr.Zero);
                if (!ok || transferred != packet.Length)
                {
                    SetLastError("WinUsb_WritePipe display failed", Marshal.GetLastWin32Error());
                    return 0;
                }

                return 1;
            }
            catch (Exception ex)
            {
                SetLastError("ShowBraille exception: " + ex.Message);
                return 0;
            }
        }

        internal static int GetBtn()
        {
            try
            {
                if (winUsbHandle == IntPtr.Zero)
                {
                    SetLastError("GetBtn called while disconnected");
                    return -1;
                }

                byte[] request = new byte[] { 0x81 };
                uint transferred;
                if (!WinUsb_WritePipe(winUsbHandle, OutPipeId, request, 1, out transferred, IntPtr.Zero) || transferred != 1)
                {
                    SetLastError("WinUsb_WritePipe key request failed", Marshal.GetLastWin32Error());
                    return -1;
                }

                byte[] response = new byte[PacketSize];
                if (!WinUsb_ReadPipe(winUsbHandle, InPipeId, response, (uint)response.Length, out transferred, IntPtr.Zero))
                {
                    SetLastError("WinUsb_ReadPipe key response failed", Marshal.GetLastWin32Error());
                    return -1;
                }

                if (transferred < 2 || response[0] != 0x81)
                {
                    SetLastError("Invalid key response");
                    return -1;
                }

                return response[1];
            }
            catch (Exception ex)
            {
                SetLastError("GetBtn exception: " + ex.Message);
                return -1;
            }
        }

        internal static void EnsureLoaded()
        {
        }

        private static string FindDevicePath()
        {
            IntPtr infoSet = IntPtr.Zero;
            try
            {
                Guid interfaceGuid = DeviceInterfaceGuid;
                infoSet = SetupDiGetClassDevs(
                    ref interfaceGuid,
                    null,
                    IntPtr.Zero,
                    DigcfPresent | DigcfDeviceinterface);

                if (IsInvalidHandle(infoSet))
                {
                    SetLastError("SetupDiGetClassDevs failed", Marshal.GetLastWin32Error());
                    return null;
                }

                SP_DEVICE_INTERFACE_DATA interfaceData = new SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

                for (uint index = 0; ; index++)
                {
                    if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == ErrorNoMoreItems)
                        {
                            SetLastError("No matching device interface");
                            return null;
                        }

                        SetLastError("SetupDiEnumDeviceInterfaces failed", error);
                        continue;
                    }

                    uint requiredSize = 0;
                    SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
                    if (requiredSize == 0)
                    {
                        continue;
                    }

                    IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(infoSet, ref interfaceData, detailDataBuffer, requiredSize, out requiredSize, IntPtr.Zero))
                        {
                            IntPtr pathPointer = new IntPtr(detailDataBuffer.ToInt64() + 4);
                            string path = Marshal.PtrToStringAuto(pathPointer);
                            if (!String.IsNullOrEmpty(path))
                            {
                                return path;
                            }
                        }
                        else
                        {
                            SetLastError("SetupDiGetDeviceInterfaceDetail failed", Marshal.GetLastWin32Error());
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            catch (Exception ex)
            {
                SetLastError("FindDevicePath exception: " + ex.Message);
                return null;
            }
            finally
            {
                if (!IsInvalidHandle(infoSet))
                {
                    SetupDiDestroyDeviceInfoList(infoSet);
                }
            }
        }

        private static void SetLastError(string message)
        {
            lastError = message;
        }

        private static void SetLastError(string message, int win32Error)
        {
            lastError = message + " (Win32=" + win32Error.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private static bool IsInvalidHandle(IntPtr handle)
        {
            return handle == IntPtr.Zero || handle.ToInt64() == InvalidHandleValue;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Initialize(IntPtr DeviceHandle, out IntPtr InterfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Free(IntPtr InterfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_WritePipe(
            IntPtr InterfaceHandle,
            byte PipeID,
            byte[] Buffer,
            uint BufferLength,
            out uint LengthTransferred,
            IntPtr Overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ReadPipe(
            IntPtr InterfaceHandle,
            byte PipeID,
            byte[] Buffer,
            uint BufferLength,
            out uint LengthTransferred,
            IntPtr Overlapped);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            string Enumerator,
            IntPtr hwndParent,
            int Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet,
            IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid,
            uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            uint DeviceInterfaceDetailDataSize,
            out uint RequiredSize,
            IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }
    }
}
