using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using libeLog.Models;
using remeLog.Models;
using remeLog.ViewModels;
using remeLog.Views.Controls;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace remeLog.Views
{
    /// <summary>
    /// Логика взаимодействия для EditSerialPartsWindow.xaml
    /// </summary>
    public partial class EditSerialPartsWindow : Window
    {
        private InsertionAdorner? _insertionAdorner;
        private Point _startPoint;
        private TreeViewItem? _draggedItem;

        public EditSerialPartsWindow()
        {
            InitializeComponent();
        }

        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Сохраняем элемент и точку начала клика
            _draggedItem = sender as TreeViewItem;
            _startPoint = e.GetPosition(null);
        }

        private void TreeViewItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null)
                return;

            // Проверяем, было ли достаточно перемещение для начала перетаскивания
            Point currentPosition = e.GetPosition(null);
            Vector diff = _startPoint - currentPosition;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                if (_draggedItem.DataContext is CncOperation operation)
                {
                    var vm = (EditSerialPartsWindowViewModel)DataContext;
                    vm.DraggedOperation = operation;
                    DragDrop.DoDragDrop(_draggedItem, operation, DragDropEffects.Move);
                    vm.DraggedOperation = null;
                }
                _draggedItem = null;
            }
        }

        private void TreeViewItem_DragOver(object sender, DragEventArgs e)
        {
            var vm = (EditSerialPartsWindowViewModel)DataContext;
            if (vm.DraggedOperation == null) return;

            if (sender is not TreeViewItem item ||
                item.DataContext is not CncOperation) return;

            var position = e.GetPosition(item);
            var isAbove = position.Y < item.ActualHeight / 2;

            RemoveAdorner();
            _insertionAdorner = new InsertionAdorner(item, isAbove);
            AdornerLayer.GetAdornerLayer(item)?.Add(_insertionAdorner);
        }

        private void TreeViewItem_Drop(object sender, DragEventArgs e)
        {
            var vm = (EditSerialPartsWindowViewModel)DataContext;
            if (vm.DraggedOperation == null) return;

            if (sender is not TreeViewItem dropItem ||
                dropItem.DataContext is not CncOperation targetOperation) return;

            RemoveAdorner();

            var position = e.GetPosition(dropItem);
            bool isAbove = position.Y < dropItem.ActualHeight / 2;

            vm.MoveOperation(targetOperation, isAbove);
            e.Handled = true;
        }

        private void TreeViewItem_DragLeave(object sender, DragEventArgs e)
        {
            RemoveAdorner();
        }

        private void RemoveAdorner()
        {
            if (_insertionAdorner != null)
            {
                AdornerLayer.GetAdornerLayer(_insertionAdorner.AdornedElement)?.Remove(_insertionAdorner);
                _insertionAdorner = null;
            }
        }

        private void editSerialPartsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            (DataContext as EditSerialPartsWindowViewModel)?._cts.Cancel();
        }
    }
}
