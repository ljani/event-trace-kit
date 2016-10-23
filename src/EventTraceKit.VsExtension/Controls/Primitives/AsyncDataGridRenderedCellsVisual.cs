namespace EventTraceKit.VsExtension.Controls.Primitives
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Windows;
    using System.Windows.Media;
    using Windows;

    public class AsyncDataGridRenderedCellsVisual : DrawingVisual
    {
        private AsyncDataGrid parentGrid;
        private readonly AsyncDataGridCellsPresenter cellsPresenter;

        public AsyncDataGridRenderedCellsVisual(
            AsyncDataGridCellsPresenter cellsPresenter)
        {
            this.cellsPresenter = cellsPresenter;
            VisualTextHintingMode = TextHintingMode.Fixed;
        }

        internal void InvalidateRowCache()
        {
            rowCacheInvalid = true;
        }

        private AsyncDataGrid ParentGrid =>
            parentGrid ?? (parentGrid = cellsPresenter.FindAncestor<AsyncDataGrid>());

        public Rect RenderedViewport { get; private set; }

        internal void Update(Rect viewport, Size extentSize, bool forceUpdate)
        {
            VerifyAccess();

            Rect rect = Rect.Intersect(viewport, new Rect(extentSize));
            if (!forceUpdate && RenderedViewport.Contains(rect)) {
                Offset = RenderedViewport.Location - rect.Location;
                //cellsPresenter.SetOffsetForAllUIElements(Offset);
                return;
            }

            RenderedViewport = viewport;
            var viewModel = cellsPresenter.ViewModel;
            if (viewModel == null)
                return;

            double horizontalOffset = cellsPresenter.HorizontalOffset;
            var visibleColumns = cellsPresenter.VisibleColumns;
            int firstVisibleRow = cellsPresenter.FirstVisibleRowIndex;
            int lastVisibleRow = cellsPresenter.LastVisibleRowIndex;

            double[] columnEdges = ComputeColumnEdges(visibleColumns);

            int firstVisibleColumn = -1;
            int lastVisibleColumn = firstVisibleColumn - 1;
            for (int i = 0; i < visibleColumns.Count; ++i) {
                if (columnEdges[i + 1] >= horizontalOffset) {
                    if (firstVisibleColumn == -1)
                        firstVisibleColumn = i;
                    lastVisibleColumn = i;
                    double rightEdge = columnEdges[i + 1] - horizontalOffset;
                    if (rightEdge > viewport.Width)
                        break;
                }
            }

            firstVisibleColumn = 0;
            lastVisibleColumn = visibleColumns.Count - 1;

            int firstNonFrozenColumn = firstVisibleColumn;
            int lastNonFrozenColumn = lastVisibleColumn;
            for (int i = 0; i < visibleColumns.Count; ++i) {
                if (!visibleColumns[i].IsFrozen()) {
                    firstNonFrozenColumn = i;
                    break;
                }
            }
            for (int i = visibleColumns.Count - 1; i >= 0; --i) {
                if (!visibleColumns[i].IsFrozen()) {
                    lastNonFrozenColumn = i;
                    break;
                }
            }

            if (!EnsureReadyForViewport(
                viewModel, firstVisibleColumn, lastVisibleColumn, firstVisibleRow,
                lastVisibleRow))
                return;

            Offset = new Vector();
            RenderedViewport = Rect.Empty;
            Children.Clear();

            using (DrawingContext dc = RenderOpen()) {
                int rowCount = viewModel.RowCount;
                if (rowCount <= 0 || visibleColumns.Count <= 0)
                    return;

                double actualWidth = cellsPresenter.ActualWidth;
                double actualHeight = cellsPresenter.ActualHeight;
                double verticalOffset = cellsPresenter.VerticalOffset;
                double rowHeight = cellsPresenter.RowHeight;
                double height = Math.Min((rowCount * rowHeight) - verticalOffset, actualHeight);

                Brush frozenColumnBackground = cellsPresenter.FrozenColumnBackground;
                for (int col = firstVisibleColumn; col <= lastVisibleColumn; ++col) {
                    double leftEdge = columnEdges[col] - horizontalOffset;
                    double rightEdge = columnEdges[col + 1] - horizontalOffset;
                    double width = rightEdge - leftEdge;
                    if (visibleColumns[col].IsFrozen()) {
                        dc.DrawRectangle(
                            frozenColumnBackground,
                            null, new Rect(leftEdge, 0, width, height));
                    }
                }

                Brush primaryBackground = cellsPresenter.PrimaryBackground;
                Brush secondaryBackground = cellsPresenter.SecondaryBackground;
                for (int row = firstVisibleRow; row <= lastVisibleRow; ++row) {
                    double topEdge = (row * rowHeight) - verticalOffset;
                    var background = row % 2 == 0
                        ? primaryBackground : secondaryBackground;
                    dc.DrawRectangle(
                        background, null,
                        new Rect(0, topEdge, actualWidth, rowHeight));
                }

                Brush selectionForeground = cellsPresenter.SelectionForeground;
                Brush selectionBackground = cellsPresenter.SelectionBackground;
                Pen selectionBorderPen = cellsPresenter.SelectionBorderPen;
                if (!ParentGrid.IsSelectionActive) {
                    selectionForeground = cellsPresenter.InactiveSelectionForeground;
                    selectionBackground = cellsPresenter.InactiveSelectionBackground;
                    selectionBorderPen = cellsPresenter.InactiveSelectionBorderPen;
                }

                bool hasVisibleSelection =
                    selectionForeground != null ||
                    selectionBackground != null ||
                    selectionBorderPen != null;

                if (hasVisibleSelection) {
                    var rowSelection = viewModel.RowSelection;

                    for (int row = firstVisibleRow; row <= lastVisibleRow; ++row) {
                        if (!rowSelection.Contains(row))
                            continue;

                        double topEdge = (row * rowHeight) - verticalOffset;
                        double bottomEdge = topEdge + rowHeight - 1;

                        dc.DrawRectangle(
                            selectionBackground, null,
                            new Rect(
                                new Point(0, topEdge),
                                new Point(actualWidth, bottomEdge + 1)));

                        if (!rowSelection.Contains(row - 1)) {
                            dc.DrawLineSnapped(
                                selectionBorderPen,
                                new Point(0, topEdge),
                                new Point(actualWidth, topEdge));
                        }

                        if (!rowSelection.Contains(row + 1)) {
                            dc.DrawLineSnapped(
                                selectionBorderPen,
                                new Point(0, bottomEdge),
                                new Point(actualWidth, bottomEdge));
                        }
                    }
                }

                Pen horizontalGridLinesPen = cellsPresenter.HorizontalGridLinesPen;
                if (horizontalGridLinesPen != null) {
                    for (int row = firstVisibleRow; row <= lastVisibleRow; ++row) {
                        double bottomEdge = ((row + 1) * rowHeight) - verticalOffset;
                        dc.DrawLineSnapped(
                            horizontalGridLinesPen,
                            new Point(0, bottomEdge),
                            new Point(actualWidth, bottomEdge));
                    }
                }

                bool hasFrozenColumns = firstNonFrozenColumn > firstVisibleColumn ||
                                        lastNonFrozenColumn < lastVisibleColumn;

                if ((hasFrozenColumns && prevRenderedWidth != actualWidth) ||
                    hasFrozenColumns != prevHasFrozenColumns) {
                    frozenRowCacheInvalid = true;
                    nonFrozenAreaClip = null;
                }

                prevHasFrozenColumns = hasFrozenColumns;
                prevRenderedWidth = actualWidth;

                PreTrimRowCache(firstVisibleRow, lastVisibleRow);

                if (visibleColumns.Count > 0) {
                    RenderCells(
                        dc, viewport, actualWidth, height, columnEdges,
                        firstVisibleColumn, lastVisibleColumn,
                        firstVisibleRow, lastVisibleRow,
                        firstNonFrozenColumn, lastNonFrozenColumn);
                }

                PostTrimRowCache(firstVisibleRow, lastVisibleRow);

                frozenRowCacheInvalid = false;

                int focusIndex = viewModel.FocusIndex;
                Pen focusBorderPen = cellsPresenter.FocusBorderPen;
                if (ParentGrid.IsSelectionActive
                    && focusBorderPen != null
                    && focusIndex >= firstVisibleRow
                    && focusIndex <= lastVisibleRow) {

                    double rowX = -horizontalOffset;
                    double rowY = (focusIndex * rowHeight) - verticalOffset;

                    {
                        double rightEdge = columnEdges[columnEdges.Length - 1] - 1;
                        if (lastNonFrozenColumn < lastVisibleColumn)
                            rightEdge = actualWidth - 1;
                        double bottomEdge = rowHeight - 1;
                        var bounds = new Rect(0, 0, rightEdge, bottomEdge);

                        focusVisual = new DrawingVisual();
                        focusVisual.Transform = new TranslateTransform(0, 0);
                        var context = focusVisual.RenderOpen();
                        context.DrawRectangleSnapped(null, focusBorderPen, bounds);
                        context.Close();
                    }

                    AddAtOffset(focusVisual, rowX, rowY);
                }
            }
        }

        private void AddAtOffset(ContainerVisual visual, double x, double y)
        {
            Children.Add(visual);
            var transform = (TranslateTransform)visual.Transform;
            transform.X = x;
            transform.Y = y;
        }

        private DrawingVisual focusVisual;
        private readonly List<RenderedRow> renderedRowCache = new List<RenderedRow>();

        private struct RenderedRow
        {
            public RenderedRow(
                int rowIndex, int styleHash, DrawingVisual visual,
                DrawingVisual frozenVisual)
            {
                RowIndex = rowIndex;
                StyleHash = styleHash;
                Visual = visual;
                FrozenVisual = frozenVisual;
            }

            public int RowIndex { get; }
            public DrawingVisual Visual { get; }
            public DrawingVisual FrozenVisual { get; set; }
            public int StyleHash { get; }
        }

        private void RenderCells(
            DrawingContext context, Rect viewport, double width, double height,
            double[] columnEdges,
            int firstVisibleColumn, int lastVisibleColumn,
            int firstVisibleRow, int lastVisibleRow,
            int firstNonFrozenColumn, int lastNonFrozenColumn)
        {
            double horizontalOffset = cellsPresenter.HorizontalOffset;
            double verticalOffset = cellsPresenter.VerticalOffset;
            double rowHeight = cellsPresenter.RowHeight;
            var visibleColumns = cellsPresenter.VisibleColumns;

            Typeface typeface = cellsPresenter.Typeface;
            double fontSize = cellsPresenter.FontSize;
            FlowDirection flowDirection = cellsPresenter.FlowDirection;
            CultureInfo currentCulture = CultureInfo.CurrentCulture;
            Pen verticalGridLinesPen = cellsPresenter.VerticalGridLinesPen;
            Brush keySeparatorBrush = cellsPresenter.KeySeparatorBrush;
            Brush freezableAreaSeparatorBrush = cellsPresenter.FreezableAreaSeparatorBrush;
            Brush selectionForeground = cellsPresenter.SelectionForeground;
            if (!ParentGrid.IsSelectionActive)
                selectionForeground = cellsPresenter.InactiveSelectionForeground;
            var rowSelection = cellsPresenter.ViewModel.RowSelection;

            double padding = rowHeight / 10;
            double totalPadding = 2 * padding;

            for (int col = firstVisibleColumn; col <= lastVisibleColumn; ++col) {
                double leftEdge = columnEdges[col] - horizontalOffset;
                double rightEdge = columnEdges[col + 1] - horizontalOffset;

                if (verticalGridLinesPen != null) {
                    context.DrawLineSnapped(
                        verticalGridLinesPen,
                        new Point(leftEdge, 0),
                        new Point(leftEdge, height));
                }

                double cellWidth = rightEdge - leftEdge;
                var column = visibleColumns[col];

                if (column.IsKeySeparator) {
                    context.DrawRectangle(
                        keySeparatorBrush, null,
                        new Rect(leftEdge, 0, cellWidth, height));
                } else if (column.IsFreezableAreaSeparator) {
                    context.DrawRectangle(
                        freezableAreaSeparatorBrush, null,
                        new Rect(leftEdge, 0, cellWidth, height));
                }
            }

            double lastRightEdge = columnEdges[columnEdges.Length - 1];
            if (lastRightEdge <= viewport.Right && verticalGridLinesPen != null) {
                context.DrawLineSnapped(
                    verticalGridLinesPen,
                    new Point(lastRightEdge, 0),
                    new Point(lastRightEdge, height));
            }

            bool hasFrozenColumns = firstNonFrozenColumn > firstVisibleColumn ||
                                    lastNonFrozenColumn < lastVisibleColumn;

            int viewportSizeHint = lastVisibleRow - firstVisibleRow + 1;
            for (int row = firstVisibleRow; row <= lastVisibleRow; ++row) {
                double rowX = -horizontalOffset;
                double rowY = (row * rowHeight) - verticalOffset;

                Brush foreground = cellsPresenter.Foreground;
                if (rowSelection.Contains(row))
                    foreground = selectionForeground;

                int styleHash = ComputeHash(rowHeight, flowDirection, typeface, fontSize, foreground);

                DrawingVisual rowVisual;
                if (!TryGetCachedRow(row, styleHash, out rowVisual)) {
                    var rowContext = rowVisual.RenderOpen();

                    for (int col = firstNonFrozenColumn; col <= lastNonFrozenColumn; ++col) {
                        var column = visibleColumns[col];
                        if (column.IsKeySeparator || column.IsFreezableAreaSeparator)
                            continue;
                        if (!column.IsSafeToReadCellValuesFromUIThread)
                            continue;

                        double topEdge = 0;
                        double leftEdge = columnEdges[col];
                        double rightEdge = columnEdges[col + 1];
                        double cellWidth = rightEdge - leftEdge;

                        var value = column.GetCellValue(row, viewportSizeHint);
                        if (value != null) {
                            var formatted = new FormattedText(
                                value.ToString(), currentCulture,
                                flowDirection, typeface, fontSize, foreground, null,
                                TextFormattingMode.Display);
                            formatted.MaxTextWidth = Math.Max(cellWidth - totalPadding, 0);
                            formatted.MaxTextHeight = rowHeight;
                            formatted.TextAlignment = column.TextAlignment;
                            formatted.Trimming = TextTrimming.CharacterEllipsis;

                            if (totalPadding < cellWidth) {
                                var offsetY = (rowHeight - formatted.Height) / 2;
                                var origin = new Point(
                                    leftEdge + padding,
                                    topEdge + offsetY);
                                origin = origin.Round(MidpointRounding.AwayFromZero);
                                rowContext.DrawText(formatted, origin);
                            }
                        }
                    }

                    rowContext.Close();
                }

                AddAtOffset(rowVisual, rowX, rowY);

                if (hasFrozenColumns) {
                    if (nonFrozenAreaClip == null) {
                        double nonFrozenLeftEdge = columnEdges[firstNonFrozenColumn];
                        double nonFrozenRightEdge = width - (columnEdges[lastVisibleColumn + 1] - columnEdges[lastNonFrozenColumn + 1]);

                        var nonFrozenArea = new Rect(
                            nonFrozenLeftEdge, 0,
                            Math.Max(nonFrozenRightEdge - nonFrozenLeftEdge, 0), rowHeight);

                        nonFrozenAreaClip = new RectangleGeometry(nonFrozenArea) {
                            Transform = new TranslateTransform(horizontalOffset, 0)
                        };
                    }

                    rowVisual.Clip = nonFrozenAreaClip;
                    ((TranslateTransform)nonFrozenAreaClip.Transform).X = horizontalOffset;

                    DrawingVisual frozenRowVisual;
                    if (!TryGetCachedFrozenRow(row, styleHash, out frozenRowVisual)) {
                        var rowContext = frozenRowVisual.RenderOpen();

                        for (int col = firstVisibleColumn; col <= lastVisibleColumn; ++col) {
                            var column = visibleColumns[col];
                            if (column.IsKeySeparator || column.IsFreezableAreaSeparator)
                                continue;
                            if (!column.IsSafeToReadCellValuesFromUIThread)
                                continue;
                            if (col >= firstNonFrozenColumn && col <= lastNonFrozenColumn)
                                continue;

                            double topEdge = 0;
                            double leftEdge = columnEdges[col];
                            double rightEdge = columnEdges[col + 1];
                            double cellWidth = rightEdge - leftEdge;

                            if (col > lastNonFrozenColumn) {
                                double distance = columnEdges[lastVisibleColumn + 1] - columnEdges[col];
                                leftEdge = width - distance;
                                rightEdge = leftEdge + cellWidth;
                            }

                            var value = column.GetCellValue(row, viewportSizeHint);
                            if (value != null) {
                                var formatted = new FormattedText(
                                    value.ToString(), currentCulture,
                                    flowDirection, typeface, fontSize, foreground, null,
                                    TextFormattingMode.Display);
                                formatted.MaxTextWidth = Math.Max(cellWidth - totalPadding, 0);
                                formatted.MaxTextHeight = rowHeight;
                                formatted.TextAlignment = column.TextAlignment;
                                formatted.Trimming = TextTrimming.CharacterEllipsis;

                                if (totalPadding < cellWidth) {
                                    var offsetY = (rowHeight - formatted.Height) / 2;
                                    var origin = new Point(
                                        leftEdge + padding,
                                        topEdge + offsetY);
                                    origin = origin.Round(MidpointRounding.AwayFromZero);
                                    rowContext.DrawText(formatted, origin);
                                }
                            }
                        }

                        rowContext.Close();
                    }

                    AddAtOffset(frozenRowVisual, 0, rowY);
                }
            }
        }

        private void PreTrimRowCache(int firstVisibleRow, int lastVisibleRow)
        {
            if (renderedRowCache.Count > 0) {
                bool hasAnyRowCached =
                    renderedRowCache[0].RowIndex <= lastVisibleRow &&
                    renderedRowCache[renderedRowCache.Count - 1].RowIndex >= firstVisibleRow;
                if (!hasAnyRowCached)
                    rowCacheInvalid = true;
            }

            if (rowCacheInvalid) {
                renderedRowCache.Clear();
                rowCacheInvalid = false;
            }
        }

        private void PostTrimRowCache(int firstVisibleRow, int lastVisibleRow)
        {
            int visibleRows = lastVisibleRow - firstVisibleRow + 1;
            int overhang = Math.Min(2, visibleRows / 3);

            int firstCachedRow = firstVisibleRow - overhang;
            int lastCachedRow = lastVisibleRow + overhang;

            int begin = 0;
            int end = renderedRowCache.Count - 1;

            int validBegin = begin;
            for (int i = begin; i < renderedRowCache.Count; ++i) {
                if (renderedRowCache[i].RowIndex >= firstCachedRow) {
                    validBegin = i;
                    break;
                }
            }

            int validEnd = end;
            for (int i = end; i >= 0; --i) {
                if (renderedRowCache[i].RowIndex <= lastCachedRow) {
                    validEnd = i;
                    break;
                }
            }

            int count = end - validEnd;
            if (count != 0)
                renderedRowCache.RemoveRange(validEnd, count);

            count = validBegin - begin;
            if (count != 0)
                renderedRowCache.RemoveRange(begin, count);
        }

        private const int PrimeFactor = 397;

        private int ComputeHash(
            double value1,
            FlowDirection value2,
            Typeface value3,
            double value4,
            Brush value5)
        {
            unchecked {
                int hash = value1.GetHashCode();
                hash = (hash * PrimeFactor) ^ value2.GetHashCode();
                hash = (hash * PrimeFactor) ^ (value3?.GetHashCode() ?? 0);
                hash = (hash * PrimeFactor) ^ value4.GetHashCode();
                hash = (hash * PrimeFactor) ^ (value5?.GetHashCode() ?? 0);
                return hash;
            }
        }

        private bool TryGetCachedRow(
            int row, int styleHash, out DrawingVisual rowVisual)
        {
            int idx = renderedRowCache.BinarySearch(row, (x, y) => x.RowIndex.CompareTo(y));
            if (idx >= 0) {
                rowVisual = renderedRowCache[idx].Visual;
                if (renderedRowCache[idx].StyleHash == styleHash)
                    return true;

                renderedRowCache[idx] = new RenderedRow(row, styleHash, rowVisual, null);
                return false;
            }

            rowVisual = new DrawingVisual();
            rowVisual.Transform = new TranslateTransform(0, 0);
            renderedRowCache.Insert(~idx, new RenderedRow(row, styleHash, rowVisual, null));
            return false;
        }

        private bool TryGetCachedFrozenRow(int row, int styleHash, out DrawingVisual rowVisual)
        {
            int idx = renderedRowCache.BinarySearch(row, (x, y) => x.RowIndex.CompareTo(y));
            Debug.Assert(idx >= 0);

            var entry = renderedRowCache[idx];
            if (!frozenRowCacheInvalid && entry.StyleHash == styleHash && entry.FrozenVisual != null) {
                rowVisual = entry.FrozenVisual;
                return true;
            }

            if (frozenRowCacheInvalid || entry.FrozenVisual == null) {
                rowVisual = new DrawingVisual();
                rowVisual.Transform = new TranslateTransform(0, 0);
                entry.FrozenVisual = rowVisual;
                renderedRowCache[idx] = entry;
            } else {
                rowVisual = entry.FrozenVisual;
            }

            return false;
        }

        private double[] ComputeColumnEdges(
            IList<AsyncDataGridColumn> visibleColumns)
        {
            var edges = new double[visibleColumns.Count + 1];

            double cumulativeWidth = 0;
            for (int i = 1; i < edges.Length; ++i) {
                cumulativeWidth += visibleColumns[i - 1].Width;
                edges[i] = cumulativeWidth;
            }

            return edges;
        }

        internal double GetColumnAutoSize(AsyncDataGridColumn column)
        {
            if (!column.IsVisible && !column.IsDisconnected)
                throw new InvalidOperationException();

            double width = 5.0;

            var viewModel = cellsPresenter.ViewModel;
            if (viewModel == null || viewModel.RowCount <= 0)
                return width;

            double rowHeight = cellsPresenter.RowHeight;
            int firstVisibleRow = cellsPresenter.FirstVisibleRowIndex;
            int lastVisibleRow = cellsPresenter.LastVisibleRowIndex;
            Typeface typeface = cellsPresenter.Typeface;
            double fontSize = cellsPresenter.FontSize;
            Brush foreground = cellsPresenter.Foreground;
            FlowDirection flowDirection = cellsPresenter.FlowDirection;
            CultureInfo currentCulture = CultureInfo.CurrentCulture;

            double padding = rowHeight / 10;
            double totalPadding = 2 * padding;
            int viewportSizeHint = lastVisibleRow - firstVisibleRow + 1;

            for (int row = firstVisibleRow; row <= lastVisibleRow; ++row) {
                var value = column.GetCellValue(row, viewportSizeHint);
                var formatted = new FormattedText(
                    value.ToString(), currentCulture, flowDirection, typeface,
                    fontSize, foreground, null, TextFormattingMode.Display);

                width = Math.Max(width, formatted.Width + totalPadding + 1);
            }

            return width;
        }

        private int cachedFirstVisibleColumn;
        private int cachedLastVisibleColumn;
        private int cachedFirstVisibleRow;
        private int cachedLastVisibleRow;
        private bool lastPrefetchCancelled;
        private bool isAsyncPrefetchInProgress;
        private object cachedDataValidityToken;
        private bool rowCacheInvalid;
        private bool frozenRowCacheInvalid;
        private bool prevHasFrozenColumns;
        private double prevRenderedWidth;
        private RectangleGeometry nonFrozenAreaClip;

        private bool EnsureReadyForViewport(
            AsyncDataGridCellsPresenterViewModel dataViewModel,
            int firstVisibleColumn, int lastVisibleColumn, int firstVisibleRow, int lastVisibleRow)
        {
            VerifyAccess();
            if (isAsyncPrefetchInProgress)
                return true;

            if (cachedFirstVisibleColumn == firstVisibleColumn &&
                cachedLastVisibleColumn == lastVisibleColumn &&
                cachedFirstVisibleRow == firstVisibleRow &&
                cachedLastVisibleRow == lastVisibleRow &&
                //dataViewModel.IsValidDataValidityToken(cachedDataValidityToken) &&
                //!dataViewModel.RowSelection.IsRefreshNecessary() &&
                !lastPrefetchCancelled) {
                return true;
            }

            lastPrefetchCancelled = false;
            cachedFirstVisibleColumn = firstVisibleColumn;
            cachedLastVisibleColumn = lastVisibleColumn;
            cachedFirstVisibleRow = firstVisibleRow;
            cachedLastVisibleRow = lastVisibleRow;
            cachedDataValidityToken = dataViewModel.DataValidityToken;
            isAsyncPrefetchInProgress = true;

            Action<bool> callBackWhenFinished = wasCancelled => {
                lastPrefetchCancelled = wasCancelled;
                isAsyncPrefetchInProgress = false;
                if (!wasCancelled) {
                    cellsPresenter.QueueRender(true);
                }
            };
            Action<bool> highlightAndSelectionPrefetched = wasCancelled => {
                if (!wasCancelled) {
                    cellsPresenter.QueueRender(true);
                }
            };

            dataViewModel.PrefetchAllDataAndQueueUpdateRender(
                cellsPresenter, firstVisibleColumn, lastVisibleColumn,
                firstVisibleRow, lastVisibleRow, highlightAndSelectionPrefetched, callBackWhenFinished);
            return true; //FIXME: false
        }
    }
}
