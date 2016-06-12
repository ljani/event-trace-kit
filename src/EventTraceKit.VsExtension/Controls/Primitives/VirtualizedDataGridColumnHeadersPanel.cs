﻿namespace EventTraceKit.VsExtension.Controls.Primitives
{
    using System;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;
    using System.Windows.Media;

    public class VirtualizedDataGridColumnHeadersPanel : StackPanel
    {
        private VirtualizedDataGrid parentGrid;

        private ItemsControl ParentPresenter
        {
            get
            {
                var itemsPresenter = TemplatedParent as FrameworkElement;
                return itemsPresenter?.TemplatedParent as ItemsControl;
            }
        }

        private VirtualizedDataGrid ParentGrid =>
            parentGrid ??
            (parentGrid = (ParentPresenter as VirtualizedDataGridColumnHeadersPresenter)?.ParentGrid);

        private int LeftFrozenColumnCount => 0;
        private int RightFrozenColumnCount => 0;

        protected override Visual GetVisualChild(int index)
        {
            index = MapVisualIndex(index);
            return base.GetVisualChild(index);
        }

        private int MapVisualIndex(int index)
        {
            // The column range [b, e) we get from the generator is divided into
            // three subranges:
            //   [b, L): Left-frozen columns
            //   [L, R): Non-frozen columns
            //   [R, e): Right-frozen colums
            //
            // All columns have the same Z-index and are drawn in visual order.
            // To draw the right-aligned gripper *over* the following column
            // header we have to reverse the order of visual children. In
            // addition we have to ensure that frozen columns are drawn after
            // non-frozen columns.
            //
            // We therefore map the visual child index to get the following
            // modified visual child range:
            //   (R, L] followed by (e, R], then (L, b].

            Debug.Assert(InternalChildren.Count == VisualChildrenCount);

            int columnCount = InternalChildren.Count;
            int leftFrozenColumnCount = Math.Min(columnCount, LeftFrozenColumnCount);
            int rightFrozenColumnCount = Math.Min(columnCount, RightFrozenColumnCount);

            int nonFrozenColumnCount = columnCount - leftFrozenColumnCount - rightFrozenColumnCount;
            int firstRightFrozenIndex = leftFrozenColumnCount + nonFrozenColumnCount;

            int end;
            if (index < nonFrozenColumnCount)
                end = firstRightFrozenIndex;
            else if (index < nonFrozenColumnCount + rightFrozenColumnCount)
                end = nonFrozenColumnCount + firstRightFrozenIndex + rightFrozenColumnCount;
            else
                end = columnCount;

            return end - index - 1;
        }

        protected override void OnIsItemsHostChanged(bool oldIsItemsHost, bool newIsItemsHost)
        {
            base.OnIsItemsHostChanged(oldIsItemsHost, newIsItemsHost);

            ItemsControl parentPresenter = ParentPresenter;
            if (newIsItemsHost) {
                var headersPresenter = parentPresenter as VirtualizedDataGridColumnHeadersPresenter;
                IItemContainerGenerator generator = parentPresenter?.ItemContainerGenerator;
                if (headersPresenter != null
                    && generator != null
                    && generator == generator.GetItemContainerGeneratorForPanel(this))
                    headersPresenter.InternalItemsHost = this;
            } else {
                var headersPresenter = parentPresenter as VirtualizedDataGridColumnHeadersPresenter;
                if (headersPresenter != null
                    && headersPresenter.InternalItemsHost == this)
                    headersPresenter.InternalItemsHost = null;
            }
        }

        protected override Size MeasureOverride(Size constraint)
        {
            Size measureSize = base.MeasureOverride(constraint);
            if (!double.IsInfinity(constraint.Width))
                measureSize.Width = constraint.Width;
            return measureSize;
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            bool isHorizontal = Orientation == Orientation.Horizontal;
            if (!isHorizontal)
                return base.ArrangeOverride(arrangeSize);

            var children = InternalChildren;
            double arrangeLength = Math.Max(arrangeSize.Height, 0);

            int leftFrozenColumnCount = Math.Min(children.Count, LeftFrozenColumnCount);
            int rightFrozenColumnCount = Math.Min(children.Count, RightFrozenColumnCount);

            var childRect = new Rect();
            childRect.X = 0;
            childRect.Y = 0;

            for (int i = 0; i < leftFrozenColumnCount; ++i) {
                var child = children[i];
                var childDesiredSize = child.DesiredSize;

                childRect.Width = childDesiredSize.Width;
                childRect.Height = Math.Max(childDesiredSize.Height, arrangeLength);

                child.Arrange(childRect);

                childRect.X += childRect.Width;
            }

            double firstNonFrozenX = childRect.X;

            childRect.X = arrangeSize.Width;
            childRect.Y = 0;

            for (int i = children.Count - 1; i >= children.Count - rightFrozenColumnCount; --i) {
                var child = children[i];
                var childDesiredSize = child.DesiredSize;

                childRect.Width = childDesiredSize.Width;
                childRect.Height = Math.Max(childDesiredSize.Height, arrangeLength);
                childRect.X -= childRect.Width;

                child.Arrange(childRect);
            }

            childRect.X = firstNonFrozenX - ParentGrid.HorizontalScrollOffset;
            childRect.Y = 0;

            for (int i = leftFrozenColumnCount; i < children.Count - rightFrozenColumnCount; ++i) {
                var child = children[i];
                var childDesiredSize = child.DesiredSize;

                childRect.Width = childDesiredSize.Width;
                childRect.Height = Math.Max(childDesiredSize.Height, arrangeLength);

                child.Arrange(childRect);

                childRect.X += childRect.Width;
            }

            return arrangeSize;
        }
    }
}