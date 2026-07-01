using DeusaldSharp;

namespace App
{
    /// <summary>
    /// Custom Window that intercepts the OS close button and prompts the user
    /// to save unsaved changes before quitting.
    /// </summary>
    public class AppWindow(Page page, ProjectStateService projectState) : Window(page)
    {
        protected override void OnHandlerChanged()
        {
            base.OnHandlerChanged();

            #if WINDOWS
            // On Windows, subscribe to the WinUI AppWindow Closing event so we
            // can show a native dialog and cancel the close if needed.
            if (Handler?.PlatformView is Microsoft.UI.Xaml.Window winUiWindow)
            {
                winUiWindow.AppWindow.Closing += OnWindowClosing;
            }
            #endif
        }

        #if WINDOWS
        private void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
                                           Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            InnerOnWindowClosing(sender, args).Forget();
        }
        
        private async Task InnerOnWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
                                           Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (!projectState.HasProject || !projectState.IsDirty || projectState.IsOnline)
                return;

            args.Cancel = true;

            bool? result = await ShowSavePromptAsync();

            if (result == true)
                await SaveAsync();
            else if (result == null)
                return; // Cancel — leave window open

            sender.Closing -= OnWindowClosing;
            sender.Destroy();
        }

        private async Task<bool?> ShowSavePromptAsync()
        {
            string action = await Application.Current!.Windows[0].Page!.DisplayActionSheetAsync(
                                title: "Unsaved changes",
                                cancel: "Cancel",
                                destruction: "Discard changes",
                                buttons: ["Save and close"]);

            return action switch
            {
                "Save and close"  => true,
                "Discard changes" => false,
                _                 => null,
            };
        }
        #else
        // macOS: override the cross-platform close-requested handler.
        protected override bool OnCloseRequested()
        {
            if (!projectState.HasProject || !projectState.IsDirty || projectState.IsOnline)
                return false; // false = allow close

            // Fire-and-forget the async prompt; cancel the immediate close and
            // re-trigger programmatically after user responds.
            _ = PromptAndCloseMacAsync();
            return true; // true = cancel the close for now
        }

        private async Task PromptAndCloseMacAsync()
        {
            string action = await Application.Current!.Windows[0].Page!.DisplayActionSheetAsync(
                title:       "Unsaved changes",
                cancel:      "Cancel",
                destruction: "Discard changes",
                buttons: ["Save and close"]);

            if (action == "Save and close")
            {
                await SaveAsync();
                Close();
            }
            else if (action == "Discard changes")
            {
                Close();
            }
            // "Cancel" — do nothing, window stays open
        }
        #endif

        private async Task SaveAsync()
        {
            await projectState.SaveAsync();
        }
    }
}