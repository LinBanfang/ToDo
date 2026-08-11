using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo;

public partial class MainWindow
{
    private void AddStepBox_ButtonClick(object sender, RoutedEventArgs e)
    {
        AddStepBox.Focus();
    }

    private void AddStepBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)
            && ViewModel.SelectedTask != null)
        {
            ViewModel.AddStepCommand.Execute((ViewModel.SelectedTask, tb.Text.Trim()));
            tb.Text = "";
            e.Handled = true;
        }
    }

    private void MyDayToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask != null)
            ViewModel.ToggleMyDayCommand.Execute(ViewModel.SelectedTask);
    }

    private void StepToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask != null)
            ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
    }

    private void StepTitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskStep step)
        {
            step.EditTitle = step.Title;
            step.IsEditing = true;
        }
    }

    private void StepEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskStep step)
        {
            if (e.Key == Key.Enter)
            {
                CommitStepEdit(step);
                if (ViewModel.SelectedTask != null)
                    ViewModel.InsertStepAfter(ViewModel.SelectedTask, step.Order);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { step.IsEditing = false; e.Handled = true; }
        }
    }

    private void StepEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskStep step)
            CommitStepEdit(step);
    }

    private void CommitStepEdit(TaskStep step)
    {
        var n = step.EditTitle?.Trim();
        if (!string.IsNullOrEmpty(n) && n != step.Title)
        {
            step.Title = n;
            if (ViewModel.SelectedTask != null)
                ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        }
        step.IsEditing = false;
    }

    // ─── Step handle: drag to reorder, click for menu ────
    private void StepHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe
            && fe.DataContext is TaskStep step && DragThresholdExceeded(e))
            DragDrop.DoDragDrop(fe, step, DragDropEffects.Move);
    }

    private void StepHandle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TaskStep step
            || ViewModel.SelectedTask == null) return;

        var menu = new ContextMenu { PlacementTarget = fe as UIElement };
        var completeItem = new MenuItem { Header = step.Completed ? Loc.MarkIncomplete : Loc.Complete };
        completeItem.Click += (_, _) => { step.Completed = !step.Completed; ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask); };
        menu.Items.Add(completeItem);
        menu.Items.Add(new Separator());
        var promoteItem = new MenuItem { Header = Loc.PromoteToTask };
        promoteItem.Click += (_, _) =>
        {
            if (ViewModel.SelectedTask != null)
                ViewModel.PromoteStepToTaskCommand.Execute((ViewModel.SelectedTask, step));
        };
        menu.Items.Add(promoteItem);
        menu.Items.Add(new Separator());
        var deleteItem = new MenuItem { Header = Loc.Delete };
        deleteItem.Click += (_, _) => ViewModel.DeleteStepCommand.Execute((ViewModel.SelectedTask!, step));
        menu.Items.Add(deleteItem);
        menu.IsOpen = true;
    }

    private Border? _lastStepDropRow;

    private void StepRow_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskStep)) && sender is Border border)
        {
            e.Effects = DragDropEffects.Move;
            UpdateStepRowDropIndicator(border, e);
        }
        e.Handled = true;
    }

    private void StepRow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskStep)) && sender is Border border)
        {
            e.Effects = DragDropEffects.Move;
            UpdateStepRowDropIndicator(border, e);
        }
        e.Handled = true;
    }

    private void StepRow_DragLeave(object sender, DragEventArgs e)
    {
        ClearStepRowDropIndicator();
        e.Handled = true;
    }

    private void UpdateStepRowDropIndicator(Border border, DragEventArgs e)
    {
        ClearStepRowDropIndicator();
        bool lowerHalf = e.GetPosition(border).Y > border.ActualHeight / 2;
        border.BorderBrush = (Brush)Application.Current.FindResource("AccentBlue");
        border.BorderThickness = new Thickness(0, lowerHalf ? 0 : 2, 0, lowerHalf ? 2 : 0);
        _lastStepDropRow = border;
    }

    private void ClearStepRowDropIndicator()
    {
        if (_lastStepDropRow != null)
        {
            _lastStepDropRow.BorderBrush = Brushes.Transparent;
            _lastStepDropRow.BorderThickness = new Thickness(0);
            _lastStepDropRow = null;
        }
    }

    private void StepRow_Drop(object sender, DragEventArgs e)
    {
        ClearStepRowDropIndicator();
        if (sender is Border border && border.DataContext is TaskStep target
            && ViewModel.SelectedTask != null
            && e.Data.GetDataPresent(typeof(TaskStep))
            && e.Data.GetData(typeof(TaskStep)) is TaskStep dragged && dragged.Id != target.Id)
        {
            var steps = ViewModel.SelectedTask.Steps;
            // Upper half of the target row inserts before it, lower half after it
            bool lowerHalf = e.GetPosition(border).Y > border.ActualHeight / 2;
            if (!ReorderService.Reorder(steps, dragged, target, lowerHalf))
            {
                e.Handled = true;
                return;
            }
            ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        }
        e.Handled = true;
    }

    private void StepDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskStep step
            && ViewModel.SelectedTask != null)
        {
            ViewModel.DeleteStepCommand.Execute((ViewModel.SelectedTask, step));
        }
    }

}
