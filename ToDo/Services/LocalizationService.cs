using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ToDo.Services;

public enum AppLanguage { English, Chinese }

public static class Loc
{
    public static AppLanguage Language { get; private set; } = AppLanguage.Chinese;
    public static event Action? LanguageChanged;

    public static void SetLanguage(AppLanguage lang)
    {
        if (Language != lang)
        {
            Language = lang;
            LanguageChanged?.Invoke();
        }
    }

    public static void Toggle()
    {
        SetLanguage(Language == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese);
    }

    // ─── String properties ────────────────────────────────
    public static string AppTitle => Language == AppLanguage.Chinese ? "待办事项" : "To Do";
    public static string Search => Language == AppLanguage.Chinese ? "搜索" : "Search";
    public static string System => Language == AppLanguage.Chinese ? "系统" : "SYSTEM";
    public static string Lists => Language == AppLanguage.Chinese ? "列表" : "LISTS";
    public static string Tags => Language == AppLanguage.Chinese ? "标签" : "TAGS";
    public static string AddTask => Language == AppLanguage.Chinese ? "添加任务" : "Add a task";
    public static string NewList => Language == AppLanguage.Chinese ? "新建列表..." : "New list...";
    public static string NewGroup => Language == AppLanguage.Chinese ? "新建分组" : "New group";
    public static string TaskDetails => Language == AppLanguage.Chinese ? "任务详情" : "Task Details";
    public static string Steps => Language == AppLanguage.Chinese ? "步骤" : "STEPS";
    public static string AddStep => Language == AppLanguage.Chinese ? "添加步骤" : "Add a step";
    public static string Closed => Language == AppLanguage.Chinese ? "已关闭" : "CLOSED";
    public static string Completed => Language == AppLanguage.Chinese ? "已完成" : "Completed";
    public static string Cancelled => Language == AppLanguage.Chinese ? "已取消" : "Cancelled";
    public static string EditCloseTime => Language == AppLanguage.Chinese ? "编辑关闭时间..." : "Edit close time...";
    public static string Notes => Language == AppLanguage.Chinese ? "备注" : "NOTES";
    public static string Delete => Language == AppLanguage.Chinese ? "删除" : "Delete";
    public static string Complete => Language == AppLanguage.Chinese ? "完成" : "Complete";
    public static string Cancel => Language == AppLanguage.Chinese ? "取消" : "Cancel";
    public static string AddToMyDay => Language == AppLanguage.Chinese ? "添加到我的一天" : "Add to My Day";
    public static string RemoveFromMyDay => Language == AppLanguage.Chinese ? "从我的一天移除" : "Remove from My Day";
    public static string MarkImportant => Language == AppLanguage.Chinese ? "标记为重要" : "Mark as important";
    public static string RemoveImportance => Language == AppLanguage.Chinese ? "取消重要标记" : "Remove importance";
    public static string MoveToList => Language == AppLanguage.Chinese ? "移动到列表" : "Move to list";
    public static string MoveToGroup => Language == AppLanguage.Chinese ? "移动到分组" : "Move to group";
    public static string Ungrouped => Language == AppLanguage.Chinese ? "未分组" : "Ungrouped";
    public static string RemoveFromGroup => Language == AppLanguage.Chinese ? "从分组移出" : "Remove from group";
    public static string ReopenTask => Language == AppLanguage.Chinese ? "重新打开" : "Reopen task";
    public static string DeleteTask => Language == AppLanguage.Chinese ? "删除任务" : "Delete task";
    public static string DeleteGroup => Language == AppLanguage.Chinese ? "删除分组" : "Delete group";
    public static string Rename => Language == AppLanguage.Chinese ? "重命名" : "Rename";
    public static string RenameList => Language == AppLanguage.Chinese ? "重命名列表" : "Rename list";
    public static string DeleteList => Language == AppLanguage.Chinese ? "删除列表" : "Delete list";
    public static string ManageTags => Language == AppLanguage.Chinese ? "管理标签" : "Manage Tags";
    public static string AddTag => Language == AppLanguage.Chinese ? "添加标签" : "Add tag";
    public static string NoTags => Language == AppLanguage.Chinese ? "暂无标签" : "No tags";
    public static string ConfirmDelete => Language == AppLanguage.Chinese ? "确认删除" : "Delete";
    public static string ConfirmDeleteMsg(string name) =>
        Language == AppLanguage.Chinese ? $"确定删除 \"{name}\" 吗？" : $"Delete \"{name}\"?";
    public static string ConfirmDeleteGroupMsg(string name) =>
        Language == AppLanguage.Chinese ? $"确定删除分组 \"{name}\" 吗？任务将变为未分组。" : $"Delete group \"{name}\"? Tasks will become ungrouped.";
    public static string CompletedSection => Language == AppLanguage.Chinese ? "已完成" : "Completed";
    public static string EditTime => Language == AppLanguage.Chinese ? "编辑关闭时间" : "Edit Close Time";
    public static string Date => Language == AppLanguage.Chinese ? "日期" : "Date";
    public static string Time => Language == AppLanguage.Chinese ? "时间" : "Time";
    public static string Save => Language == AppLanguage.Chinese ? "保存" : "Save";
    public static string TagPlaceholder => Language == AppLanguage.Chinese ? "新标签名" : "New tag name";
    public static string Add => Language == AppLanguage.Chinese ? "添加" : "Add";
    public static string Done => Language == AppLanguage.Chinese ? "完成" : "Done";
    public static string Overdue => Language == AppLanguage.Chinese ? "逾期" : "Overdue";
    public static string Today => Language == AppLanguage.Chinese ? "今天" : "Today";
    public static string Tomorrow => Language == AppLanguage.Chinese ? "明天" : "Tomorrow";
    public static string ThisWeek => Language == AppLanguage.Chinese ? "下周" : "Next Week";
    public static string Later => Language == AppLanguage.Chinese ? "稍后" : "Later";
    public static string PickDate => Language == AppLanguage.Chinese ? "选择日期..." : "Pick a date...";
    public static string NoDueDate => Language == AppLanguage.Chinese ? "无截止日期" : "No Due Date";
}
