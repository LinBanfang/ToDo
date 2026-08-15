using System;
using ToDo.Models;

namespace ToDo.Views;

/// <summary>Sample data used only by the XAML designer to preview
/// <see cref="DetailPaneControl"/> without running the app. Referenced via the d: namespace
/// (which mc:Ignorable strips at runtime), so it has no effect on the shipped app.</summary>
public static class DetailPaneDesignData
{
    public static TaskItem SampleTask { get; } = CreateSample();

    private static TaskItem CreateSample()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var task = new TaskItem
        {
            Id = "design-sample",
            Title = "设计器预览：示例任务",
            Note = "这是一段备注，用来预览详情面板的排版。",
            IsMyDay = true,
            DueDate = now + 2 * 24 * 60 * 60 * 1000L,
            Reminder = now + 3 * 60 * 60 * 1000L,
            Recurrence = RecurrenceFrequency.Weekly,
        };
        task.Steps.Add(new TaskStep { Title = "第一步（已完成）", Completed = true, Order = 1 });
        task.Steps.Add(new TaskStep { Title = "第二步（进行中）", Order = 2 });
        task.Steps.Add(new TaskStep { Title = "第三步", Order = 3 });
        return task;
    }
}
