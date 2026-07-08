using DeusaldLocalizerWeb;
#if WINDOWS
using DeusaldSharp;
#endif

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
            #elif MACCATALYST
            // On macOS, MAUI exposes no cancellable close event, so hook AppKit's
            // applicationShouldTerminate: (fired by the red close button and ⌘Q).
            MacCloseGuard.Install(
                hasUnsavedChanges: () => projectState.HasProject && projectState.IsDirty,
                prompt:            PromptAndCloseMac);
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
            if (!projectState.HasProject || !projectState.IsDirty)
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
        #elif MACCATALYST
        // Cancels the pending terminate, prompts the user, and re-issues the terminate
        // (via MacCloseGuard) once they choose Save or Discard.
        private async void PromptAndCloseMac()
        {
            bool? result = await ShowSavePromptAsync();

            if (result == true)
            {
                await SaveAsync();
                MacCloseGuard.Terminate();
            }
            else if (result == false)
            {
                MacCloseGuard.Terminate();
            }
            // null = Cancel — leave window open.
        }
        #endif

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

        private async Task SaveAsync()
        {
            await projectState.SaveAsync();
        }
    }
}
