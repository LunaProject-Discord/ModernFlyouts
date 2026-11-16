using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ModernFlyouts.Core.Interop
{
    /// <summary>
    /// Snapshot of the current virtual desktop.
    /// </summary>
    public sealed record VirtualDesktopInfo(Guid Id, int Index, int Count, string Name);

    /// <summary>
    /// Reads the current virtual desktop through the shell's internal
    /// IVirtualDesktopManagerInternal interface. The interface is undocumented and its IID
    /// changes between Windows builds, so every call is guarded and a failure simply makes
    /// <see cref="TryGetCurrentDesktop"/> return <c>null</c>.
    /// Only read-only methods are used; no notification sink is registered because the
    /// callback vtable is the part that changes most often between builds.
    /// </summary>
    public sealed class VirtualDesktopManager
    {
        private static readonly Guid CLSID_ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
        private static readonly Guid CLSID_VirtualDesktopManagerInternal = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

        // Windows 11 24H2 (26100) and later; verified on 26300.
        private static readonly Guid IID_IVirtualDesktopManagerInternal = new("53F5CA0B-158F-4124-900C-057158060B27");
        private static readonly Guid IID_IVirtualDesktop = new("3F07F4BE-B107-441A-AF0F-39D82529072C");

        // vtable slots (0..2 are IUnknown)
        private const int ManagerGetCurrentDesktopSlot = 6;
        private const int ManagerGetDesktopsSlot = 7;
        private const int DesktopGetIdSlot = 4;
        private const int DesktopGetNameSlot = 5;
        private const int ObjectArrayGetCountSlot = 3;
        private const int ObjectArrayGetAtSlot = 4;

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider10
        {
            [PreserveSig]
            int QueryService(ref Guid service, ref Guid riid, out IntPtr obj);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetPointerFn(IntPtr self, out IntPtr value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetGuidFn(IntPtr self, out Guid value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetIntFn(IntPtr self, out int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetAtFn(IntPtr self, int index, ref Guid iid, out IntPtr value);

        [DllImport("combase.dll")]
        private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        public static VirtualDesktopManager Instance { get; } = new();

        private bool isUnavailable;

        private VirtualDesktopManager()
        {
        }

        public VirtualDesktopInfo TryGetCurrentDesktop()
        {
            if (isUnavailable)
            {
                return null;
            }

            IntPtr manager = IntPtr.Zero;
            IntPtr current = IntPtr.Zero;
            IntPtr desktops = IntPtr.Zero;

            try
            {
                manager = GetManager();
                if (manager == IntPtr.Zero)
                {
                    return null;
                }

                if (Slot<GetPointerFn>(manager, ManagerGetCurrentDesktopSlot)(manager, out current) != 0 || current == IntPtr.Zero)
                {
                    return null;
                }

                Slot<GetGuidFn>(current, DesktopGetIdSlot)(current, out Guid currentId);
                string name = GetName(current);

                int index = -1, count = 0;
                if (Slot<GetPointerFn>(manager, ManagerGetDesktopsSlot)(manager, out desktops) == 0 && desktops != IntPtr.Zero)
                {
                    Slot<GetIntFn>(desktops, ObjectArrayGetCountSlot)(desktops, out count);
                    var desktopIid = IID_IVirtualDesktop;
                    for (int i = 0; i < count; i++)
                    {
                        if (Slot<GetAtFn>(desktops, ObjectArrayGetAtSlot)(desktops, i, ref desktopIid, out IntPtr desktop) != 0)
                        {
                            continue;
                        }

                        Slot<GetGuidFn>(desktop, DesktopGetIdSlot)(desktop, out Guid id);
                        Marshal.Release(desktop);

                        if (id == currentId)
                        {
                            index = i;
                            break;
                        }
                    }
                }

                return new VirtualDesktopInfo(currentId, index, count, name);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{nameof(VirtualDesktopManager)}: {ex.Message}");
                return null;
            }
            finally
            {
                if (desktops != IntPtr.Zero) Marshal.Release(desktops);
                if (current != IntPtr.Zero) Marshal.Release(current);
                if (manager != IntPtr.Zero) Marshal.Release(manager);
            }
        }

        private IntPtr GetManager()
        {
            var shellType = Type.GetTypeFromCLSID(CLSID_ImmersiveShell);
            if (shellType == null || Activator.CreateInstance(shellType) is not IServiceProvider10 shell)
            {
                return IntPtr.Zero;
            }

            try
            {
                var clsid = CLSID_VirtualDesktopManagerInternal;
                var iid = IID_IVirtualDesktopManagerInternal;
                int hr = shell.QueryService(ref clsid, ref iid, out IntPtr manager);
                if (hr != 0)
                {
                    // Unknown build: stop trying instead of guessing at vtables.
                    isUnavailable = true;
                    Debug.WriteLine($"{nameof(VirtualDesktopManager)}: QueryService failed 0x{hr:X8}");
                    return IntPtr.Zero;
                }

                return manager;
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
        }

        private static string GetName(IntPtr desktop)
        {
            if (Slot<GetPointerFn>(desktop, DesktopGetNameSlot)(desktop, out IntPtr hstring) != 0 || hstring == IntPtr.Zero)
            {
                return string.Empty;
            }

            try
            {
                IntPtr buffer = WindowsGetStringRawBuffer(hstring, out uint length);
                return Marshal.PtrToStringUni(buffer, (int)length) ?? string.Empty;
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }

        private static T Slot<T>(IntPtr obj, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        }
    }
}
