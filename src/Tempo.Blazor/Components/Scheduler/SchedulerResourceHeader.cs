using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Avatars;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The default resource header both grids share: an initials avatar, the name and the resource type.
/// A host template replaces the whole header.
/// </summary>
internal static class SchedulerResourceHeader
{
    public static RenderFragment Render(TmScheduleResource resource, RenderFragment<TmScheduleResource>? template, AvatarSize avatarSize = AvatarSize.Md)
        => builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "tm-scheduler-resource-header");
            if (template is not null)
            {
                builder.AddContent(2, template(resource));
                builder.CloseElement();
                return;
            }

            builder.OpenElement(3, "span");
            builder.AddAttribute(4, "class", "tm-scheduler-resource-header-avatar");
            builder.AddAttribute(5, "aria-hidden", "true");
            // The avatar renders its own root, so the wrapper is what assistive tech skips.
            builder.OpenComponent<TmAvatar>(6);
            builder.AddComponentParameter(7, nameof(TmAvatar.Name), resource.Name);
            builder.AddComponentParameter(8, nameof(TmAvatar.Size), avatarSize);
            builder.CloseComponent();
            builder.CloseElement();

            builder.OpenElement(9, "span");
            builder.AddAttribute(10, "class", "tm-scheduler-resource-header-text");
            builder.OpenElement(11, "span");
            builder.AddAttribute(12, "class", "tm-scheduler-resource-header-name");
            builder.AddContent(13, resource.Name);
            builder.CloseElement();
            if (!string.IsNullOrWhiteSpace(resource.ResourceType))
            {
                builder.OpenElement(14, "span");
                builder.AddAttribute(15, "class", "tm-scheduler-resource-header-type");
                builder.AddContent(16, resource.ResourceType);
                builder.CloseElement();
            }

            builder.CloseElement();
            builder.CloseElement();
        };
}
