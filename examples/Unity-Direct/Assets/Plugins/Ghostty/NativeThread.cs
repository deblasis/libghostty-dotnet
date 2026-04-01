using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ghostty.Unity
{
    /// <summary>
    /// Runs a delegate on a Win32 thread created with kernel32.CreateThread.
    /// Unity's Mono runtime creates threads with small stacks and no guard pages
    /// compatible with Zig's stack probing. This helper bypasses Mono entirely.
    /// </summary>
    public static class NativeThread
    {
        private const uint StackSize = 128 * 1024 * 1024; // 128MB
        private const uint INFINITE = 0xFFFFFFFF;

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateThread(
            IntPtr lpThreadAttributes,
            uint dwStackSize,
            IntPtr lpStartAddress,
            IntPtr lpParameter,
            uint dwCreationFlags,
            out uint lpThreadId);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ThreadProc(IntPtr lpParameter);

        /// <summary>
        /// Run an action on a native Win32 thread with a 16MB stack.
        /// Blocks until the action completes.
        /// </summary>
        public static void Run(Action action)
        {
            Exception caught = null;

            ThreadProc proc = _ =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                return 0;
            };

            // Pin the delegate so GC doesn't collect it while the native thread runs
            var gcHandle = GCHandle.Alloc(proc);
            try
            {
                var fnPtr = Marshal.GetFunctionPointerForDelegate(proc);
                var hThread = CreateThread(IntPtr.Zero, StackSize, fnPtr, IntPtr.Zero, 0, out _);
                if (hThread == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"CreateThread failed (error {Marshal.GetLastWin32Error()})");

                WaitForSingleObject(hThread, INFINITE);
                CloseHandle(hThread);
            }
            finally
            {
                gcHandle.Free();
            }

            if (caught != null)
                throw caught;
        }
    }
}
