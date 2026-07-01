using DeusaldLocalizerCommon;

namespace App
{
    /// <summary>
    /// Custom Window that intercepts the OS close button and prompts the user
    /// to save unsaved changes before quitting.
    /// </summary>
    public class AppWindow(Page page, ProjectStateService projectState, DlocFileService dlocService) : Window(page)
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
        private async void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
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
                title:       "Unsaved changes",
                cancel:      "Cancel",
                destruction: "Discard changes",
                buttons:     new[] { "Save and close" });

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
            if (!_ProjectState.HasProject || !_ProjectState.IsDirty || _ProjectState.IsOnline)
                return false; // false = allow close

            // Fire-and-forget the async prompt; cancel the immediate close and
            // re-trigger programmatically after user responds.
            _ = PromptAndCloseMacAsync();
            return true; // true = cancel the close for now
        }

        private async Task PromptAndCloseMacAsync()
        {
            string action = await Application.Current!.MainPage!.DisplayActionSheetAsync(
                title:       "Unsaved changes",
                cancel:      "Cancel",
                destruction: "Discard changes",
                buttons:     new[] { "Save and close" });

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
            ProjectDto project = projectState.CurrentProject!;

            if (string.IsNullOrEmpty(projectState.CurrentFilePath))
            {
                // New project never saved — use FileSaver to pick a location.
                using var stream = new MemoryStream();
                await dlocService.SaveToStreamAsync(project, stream);
                stream.Position = 0;

                string fileName = string.IsNullOrEmpty(project.Slug) ? "project" : project.Slug;
                var result = await CommunityToolkit.Maui.Storage.FileSaver.Default
                    .SaveAsync($"{fileName}{DlocFileService.FILE_EXTENSION}", stream);

                if (result.IsSuccessful)
                {
                    projectState.UpdateFilePath(result.FilePath);
                    projectState.MarkClean();
                }
            }
            else
            {
                await dlocService.SaveAsync(project, projectState.CurrentFilePath);
                projectState.MarkClean();
            }
        }
    }
}