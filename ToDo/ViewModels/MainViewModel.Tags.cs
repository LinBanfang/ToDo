using CommunityToolkit.Mvvm.Input;
using ToDo.Models;
using ToDo.Services;

namespace ToDo.ViewModels;

public partial class MainViewModel
{
    // ─── Tag Management ───────────────────────────────────
    [RelayCommand]
    private void CreateTag((string name, string color) param)
    {
        var name = param.name.Trim();
        // The tags collection has a unique index on Name: inserting a duplicate throws
        // a LiteException that would crash the app. Reject it up front instead.
        if (Tags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            DiagnosticLog.Warn("tags", $"ignoring duplicate tag name '{name}'");
            return;
        }
        var tag = new Tag { Name = name, Color = param.color };
        _db.Tags.Insert(tag);
        LoadTags();
    }

    [RelayCommand]
    private void UpdateTag(Tag tag)
    {
        // Same unique-index guard as CreateTag, for a rename that collides with another tag.
        if (Tags.Any(t => t.Id != tag.Id && string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)))
        {
            DiagnosticLog.Warn("tags", $"ignoring rename of '{tag.Id}' to duplicate name '{tag.Name}'");
            return;
        }
        _db.Tags.Update(tag);
        LoadTags();
    }

    [RelayCommand]
    private void DeleteTag(Tag tag)
    {
        // Remove from all tasks
        var affected = Tasks.Where(t => t.TagIds.Contains(tag.Id)).ToList();
        foreach (var t in affected)
        {
            t.TagIds.Remove(tag.Id);
            t.NotifyTagsChanged();
            _db.Tasks.Update(t);
        }
        _db.Tags.Delete(tag.Id);
        LoadTags();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void AddTagToTask((TaskItem task, Tag tag) param)
    {
        if (!param.task.TagIds.Contains(param.tag.Id))
        {
            param.task.TagIds.Add(param.tag.Id);
            param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            param.task.NotifyTagsChanged();
            _db.Tasks.Update(param.task);
            RefreshActiveTasks();
        }
    }

    [RelayCommand]
    private void RemoveTagFromTask((TaskItem task, Tag tag) param)
    {
        param.task.TagIds.Remove(param.tag.Id);
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyTagsChanged();
        _db.Tasks.Update(param.task);
        RefreshActiveTasks();
    }

    // ─── Dialog Toggles ───────────────────────────────────
    [RelayCommand]
    private void OpenTagDialog() => IsTagDialogOpen = true;

    [RelayCommand]
    private void CloseTagDialog() => IsTagDialogOpen = false;

    [RelayCommand]
    private void OpenDateTimeDialog(TaskItem task)
    {
        DateTimeTargetTask = task;
        IsDateTimeDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDateTimeDialog() => IsDateTimeDialogOpen = false;

    // ─── Theme ────────────────────────────────────────────
    [RelayCommand]
    private void ToggleTheme()
    {
        Theme = Theme == "Light" ? "Dark" : "Light";
        SettingsService.Current.Theme = Theme;
        SettingsService.Save();
        ThemeService.Apply(Theme);
    }

    // ─── Settings page ────────────────────────────────────
    [RelayCommand]
    private void OpenSettings() => IsSettingsMode = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsMode = false;
}
