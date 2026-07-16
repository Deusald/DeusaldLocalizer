using System;
using Microsoft.AspNetCore.Components;

namespace DeusaldLocalizerWeb
{
    /// <summary>
    /// Base for page components that render UI-localized text. Subscribes to
    /// <see cref="UiLocalizationService.CultureChanged"/> and re-renders when the user switches UI language.
    ///
    /// The subscription has to live on the page, not the layout: re-rendering a layout does not cascade into
    /// its <c>@Body</c>, so a layout-level refresh leaves the routed page showing stale strings until the next
    /// interaction. A page re-render, by contrast, does cascade into the page's own child components.
    ///
    /// Derived components may override <see cref="OnInitialized"/> and <see cref="Dispose"/> but must call the
    /// base implementation.
    /// </summary>
    public abstract class LocalizedComponentBase : ComponentBase, IDisposable
    {
        [Inject] protected UiLocalizationService Loc { get; set; } = default!;

        protected override void OnInitialized()
        {
            Loc.CultureChanged += OnCultureChanged;
            base.OnInitialized();
        }

        private void OnCultureChanged() => InvokeAsync(StateHasChanged);

        public virtual void Dispose() => Loc.CultureChanged -= OnCultureChanged;
    }
}
