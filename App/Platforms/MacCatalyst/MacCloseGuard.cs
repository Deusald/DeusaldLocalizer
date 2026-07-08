using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ObjCRuntime;

namespace App
{
    /// <summary>
    /// Mac Catalyst has no cross-platform, cancellable window-close event (MAUI's
    /// <see cref="Microsoft.Maui.Controls.Window"/> only exposes the non-cancellable
    /// Destroying/OnDestroying hook). To match the Windows behaviour we reach into AppKit
    /// through the Objective-C runtime and intercept
    /// <c>NSApplicationDelegate.applicationShouldTerminate:</c>, which fires both when the
    /// red close button is pressed on the last window and when the user hits ⌘Q.
    ///
    /// When there are unsaved changes we return <c>NSTerminateCancel</c> to keep the app
    /// running, show the save prompt asynchronously, and — once the user has chosen — re-issue
    /// the terminate programmatically (guarded by <see cref="_ForceQuit"/> so it passes through).
    /// </summary>
    internal static class MacCloseGuard
    {
        private const string _LIB_OBJC = "/usr/lib/libobjc.A.dylib";

        // NSApplicationTerminateReply values.
        private const nuint _NS_TERMINATE_CANCEL = 0;
        private const nuint _NS_TERMINATE_NOW    = 1;

        [DllImport(_LIB_OBJC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport(_LIB_OBJC, EntryPoint = "objc_msgSend")]
        private static extern void void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport(_LIB_OBJC, EntryPoint = "object_getClass")]
        private static extern IntPtr object_getClass(IntPtr obj);

        [DllImport(_LIB_OBJC, EntryPoint = "class_replaceMethod")]
        private static extern IntPtr class_replaceMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

        private static Func<bool>? _HasUnsavedChanges;
        private static Action?     _Prompt;
        private static IntPtr      _NsApp;
        private static bool        _Installed;
        private static bool        _ForceQuit;

        /// <summary>
        /// Installs the terminate interceptor. Safe to call more than once; only the first call
        /// takes effect.
        /// </summary>
        /// <param name="hasUnsavedChanges">Returns true when the close should be intercepted.</param>
        /// <param name="prompt">
        /// Invoked on the main thread when a close is intercepted. It should show the save prompt
        /// and, on save/discard, call <see cref="Terminate"/>.
        /// </param>
        public static unsafe void Install(Func<bool> hasUnsavedChanges, Action prompt)
        {
            if (_Installed)
                return;

            _HasUnsavedChanges = hasUnsavedChanges;
            _Prompt            = prompt;

            IntPtr nsAppClass = Class.GetHandle("NSApplication");
            _NsApp            = IntPtr_objc_msgSend(nsAppClass, Selector.GetHandle("sharedApplication"));

            if (_NsApp == IntPtr.Zero)
                return;

            IntPtr appDelegate = IntPtr_objc_msgSend(_NsApp, Selector.GetHandle("delegate"));

            if (appDelegate == IntPtr.Zero)
                return;

            IntPtr delegateClass = object_getClass(appDelegate);
            IntPtr sel           = Selector.GetHandle("applicationShouldTerminate:");

            // Q@:@  ->  returns NSUInteger (unsigned long long); args: self (id), _cmd (SEL), sender (id).
            delegate* unmanaged<IntPtr, IntPtr, IntPtr, nuint> imp = &ShouldTerminate;
            class_replaceMethod(delegateClass, sel, (IntPtr)imp, "Q@:@");

            _Installed = true;
        }

        /// <summary>Programmatically terminates the app, bypassing the unsaved-changes guard.</summary>
        public static void Terminate()
        {
            _ForceQuit = true;
            void_objc_msgSend_IntPtr(_NsApp, Selector.GetHandle("terminate:"), IntPtr.Zero);
        }

        [UnmanagedCallersOnly]
        private static nuint ShouldTerminate(IntPtr self, IntPtr sel, IntPtr sender)
        {
            if (_ForceQuit || _HasUnsavedChanges is null || !_HasUnsavedChanges())
                return _NS_TERMINATE_NOW;

            // Cancel this terminate, then prompt the user; the prompt re-issues the terminate.
            _Prompt?.Invoke();
            return _NS_TERMINATE_CANCEL;
        }
    }
}
