using System.Reflection;
using System.Numerics;
using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using Square.Rendering;
using Square.Runtime;
using Square.UI;
using Square.UI.Scrolling;
using Xunit;

namespace Square.UI.Tests;

public sealed class ScrollbarHostInteractionTests
{
    [Fact]
    public void PopupRootScrollbarIsHitTested()
    {
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var popup = new Popup { Geometry = new Rect(0, 0, 100, 100) };
        popup.Style.Set("overflow-y", "auto");
        popup.SetScrollContentSize(new Size(100, 400));
        popup.Children.Add(new View { Geometry = new Rect(0, 0, 100, 400) });
        root.Children.Add(popup);
        popup.Open();
        var tree = new DisplayTree();
        tree.BuildFrom(root);

        Assert.Same(popup, tree.HitTestScrollbar(new Point(90, 154)));
    }

    [Fact]
    public void AnchoredPopupRootScrollbarMapsScreenPointToPopupLocalCoordinates()
    {
        var window = new AppWindow("popup-root-scrollbar-host");
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var anchor = new Button { Geometry = new Rect(200, 10, 40, 20) };
        var popup = new Popup
        {
            Anchor = anchor,
            Geometry = new Rect(0, 0, 100, 100)
        };
        popup.Style.Set("overflow-y", "auto");
        popup.SetScrollContentSize(new Size(100, 400));
        popup.Children.Add(new View { Geometry = new Rect(0, 0, 100, 400) });
        root.Children.Add(anchor);
        root.Children.Add(popup);
        window.Load(root);
        popup.Open();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var localPoint = popup.GetScrollbarMetrics().VerticalThumb.Center;
        var bounds = popup.PopupBounds;
        var screenPoint = new Point(
            bounds.X + localPoint.X - popup.Geometry.X,
            bounds.Y + localPoint.Y - popup.Geometry.Y);

        InvokeHandleMouse(application, screenPoint, MouseAction.Down);

        Assert.Equal(ScrollbarPart.VerticalThumb, popup.ScrollbarInteractionPart);
    }

    [Fact]
    public void PopupHitTestMapsScrolledRootContentCoordinates()
    {
        var popup = new Popup { Geometry = new Rect(0, 0, 100, 100) };
        var child = new Button { Geometry = new Rect(0, 100, 100, 30) };
        popup.Style.Set("overflow-y", "auto");
        popup.SetScrollContentSize(new Size(100, 400));
        popup.Children.Add(child);
        popup.Open();
        popup.ScrollTop = 80;

        var point = new Point(10, popup.PopupBounds.Y + 20);

        Assert.Same(child, popup.HitTestPopup(point));
    }

    [Fact]
    public void PopupChildPointerMappingIncludesRootScrollOffset()
    {
        var popup = new Popup { Geometry = new Rect(0, 0, 100, 100) };
        var child = new View { Geometry = new Rect(0, 60, 100, 30) };
        popup.Style.Set("overflow-y", "auto");
        popup.SetScrollContentSize(new Size(100, 400));
        popup.Children.Add(child);
        popup.Open();
        popup.ScrollTop = 40;
        var screenPoint = new Point(10, popup.PopupBounds.Y + 20);
        var mapMethod = typeof(DesktopApplication).GetMethod(
            "MapPointerPoint", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(mapMethod);

        var mapped = Assert.IsType<Point>(mapMethod!.Invoke(null, [child, screenPoint]));

        Assert.Equal(new Point(10, 60), mapped);
    }

    [Fact]
    public void DesktopApplicationKeepsScrollbarDragAfterPointerLeavesBounds()
    {
        var window = new AppWindow("scrollbar-host")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Desktop
        };
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        window.Load(scroller);
        ((IComponentLifecycle)scroller).OnAttached();
        scroller.SetScrollContentSize(new Size(100, 400));

        var application = new DesktopApplication(window);
        var host = new TestHost();
        SetPrivateField(application, "_host", host);
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(scroller);

        var metrics = scroller.GetScrollbarMetrics();
        var thumbPoint = new Point(
            metrics.VerticalThumb.X + metrics.VerticalThumb.Width / 2,
            metrics.VerticalThumb.Y + metrics.VerticalThumb.Height / 2);
        InvokeHandleMouse(application, thumbPoint, MouseAction.Down);
        Assert.Equal(ScrollbarPart.VerticalThumb, scroller.ScrollbarInteractionPart);
        Assert.True(scroller.IsAttached);
        InvokeHandleMouse(application, new Point(500, 500), MouseAction.Move);

        Assert.True(scroller.ScrollTop > 0);
        Assert.Same(scroller, GetPrivateField<Element>(application, "_draggingScrollbar"));

        InvokeHandleMouse(application, new Point(500, 500), MouseAction.Up);

        Assert.Null(GetPrivateField<Element>(application, "_draggingScrollbar"));
    }

    [Fact]
    public void DesktopApplicationUpdatesScrollbarHoverForButtonlessMouseMove()
    {
        var (application, scroller) = CreateApplication();
        var metrics = scroller.GetScrollbarMetrics();

        InvokeHandleMouse(application, metrics.VerticalThumb.Center, MouseAction.Move, MouseButton.None);

        Assert.Equal(ScrollbarPart.VerticalThumb, scroller.ScrollbarHoverPart);
    }

    [Fact]
    public void ClearingScrollbarHoverToNoTargetRequestsRender()
    {
        var window = new AppWindow("scrollbar-hover-clear");
        var scroller = new NonSchedulingScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(85, 400));
        window.Load(scroller);
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(scroller);

        scroller.UpdateScrollbarHover(scroller.GetScrollbarMetrics().VerticalThumb.Center);
        SetPrivateField(application, "_hoveringScrollbar", scroller);
        SetPrivateField(application, "_renderRequested", false);
        InvokeUpdateScrollbarHover(application, new Point(10, 10));

        Assert.Equal(ScrollbarPart.None, scroller.ScrollbarHoverPart);
        Assert.True(GetPrivateField<bool>(application, "_renderRequested"));
    }

    [Fact]
    public void PressedScrollbarButtonStopsRepeatingWhenPointerLeaves()
    {
        var (application, scroller) = CreateApplication();
        var metrics = scroller.GetScrollbarMetrics();
        InvokeHandleMouse(application, metrics.VerticalForwardButton.Center, MouseAction.Down);
        Assert.Equal(40, scroller.ScrollTop);

        InvokeHandleMouse(application, new Point(500, 500), MouseAction.Move, MouseButton.None);
        SetPrivateField(application, "_nextScrollbarRepeatSeconds", 0d);
        InvokeHandleTick(application);

        Assert.Equal(40, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollbarCornerConsumesPointerDownAndUpWithoutDocumentEvents()
    {
        var window = new AppWindow("scrollbar-corner-consumption");
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow", "auto");
        scroller.SetScrollContentSize(new Size(300, 300));
        window.Load(scroller);
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(scroller);
        var events = new List<string>();
        scroller.AddEventListener(StandardEvents.PointerDown, _ => events.Add(StandardEvents.PointerDown));
        scroller.AddEventListener(StandardEvents.PointerUp, _ => events.Add(StandardEvents.PointerUp));
        scroller.AddEventListener(StandardEvents.Click, _ => events.Add(StandardEvents.Click));
        var corner = scroller.GetScrollbarMetrics().Corner.Center;

        InvokeHandleMouse(application, corner, MouseAction.Down);
        InvokeHandleTick(application);
        InvokeHandleMouse(application, corner, MouseAction.Up);

        Assert.Empty(events);
        Assert.Null(GetPrivateField<Element>(application, "_draggingScrollbar"));
        Assert.Null(GetPrivateField<Element>(application, "_pressedScrollbar"));
        Assert.Equal(0, scroller.ScrollLeft);
        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void ClickingScrollbarOutsideDismissiblePopupClosesPopup()
    {
        var window = new AppWindow("scrollbar-dismiss-popup");
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(85, 300));
        var anchor = new Button { Geometry = new Rect(220, 10, 40, 20) };
        var popup = new Popup
        {
            Geometry = new Rect(0, 0, 80, 60),
            Anchor = anchor,
            DismissOnPointerDownOutside = true
        };
        root.Children.Add(scroller);
        root.Children.Add(anchor);
        root.Children.Add(popup);
        window.Load(root);
        popup.Open();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var button = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;

        Assert.True(popup.IsPopupOpen);
        InvokeHandleMouse(application, button, MouseAction.Down);

        Assert.False(popup.IsPopupOpen);
    }

    [Fact]
    public void DesktopApplicationSchedulesSmoothWheelThroughFrameQueue()
    {
        var (application, scroller) = CreateApplication();
        scroller.AddEventListener(StandardEvents.RequestFrame, e => InvokeHandleFrameRequest(application, e));

        scroller.DispatchTrusted(StandardEvents.CreateWheel(0, 30));
        Assert.Equal(0, scroller.ScrollTop);
        Thread.Sleep(220);
        InvokeHandleTick(application);

        Assert.Equal(30, scroller.ScrollTop);
    }

    [Fact]
    public void TemporarilyHiddenScheduledTargetResumesWhenVisible()
    {
        var window = new AppWindow("hidden-frame-schedule")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Mobile
        };
        var scroller = new FrameCountingScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.SetScrollContentSize(new Size(100, 400));
        window.Load(scroller);
        var application = new DesktopApplication(window);
        scroller.AddEventListener(StandardEvents.RequestFrame, e => InvokeHandleFrameRequest(application, e));
        ((IComponentLifecycle)scroller).OnAttached();

        scroller.ScrollTop = 50;
        var scheduled = Assert.IsType<Dictionary<Element, double>>(
            GetPrivateField<Dictionary<Element, double>>(application, "_scheduledFrames"));
        Assert.True(scheduled.ContainsKey(scroller));

        scroller.IsVisible = false;
        InvokeHandleTick(application);
        InvokeHandleTick(application);

        Assert.True(scheduled.ContainsKey(scroller));
        Assert.Equal(0, scroller.FrameDueCount);

        Thread.Sleep(30);
        scroller.IsVisible = true;
        InvokeHandleTick(application);

        Assert.Equal(1, scroller.FrameDueCount);
    }

    [Fact]
    public void DesktopApplicationTargetsScrollbarInsidePopupSubtree()
    {
        var window = new AppWindow("popup-scrollbar");
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var anchor = new Button { Geometry = new Rect(20, 10, 40, 20) };
        var popup = new Popup
        {
            Geometry = new Rect(0, 0, 100, 100),
            Anchor = anchor,
            VerticalOffset = 0
        };
        var scroller = new ScrollViewer { Geometry = popup.Geometry };
        scroller.SetScrollContentSize(new Size(85, 300));
        popup.Children.Add(scroller);
        root.Children.Add(anchor);
        root.Children.Add(popup);
        window.Load(root);
        popup.Open();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var localButton = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;
        var popupBounds = popup.PopupBounds;
        var screenButton = new Point(
            localButton.X + popupBounds.X - popup.Geometry.X,
            localButton.Y + popupBounds.Y - popup.Geometry.Y);
        Assert.Same(scroller, tree.HitTestScrollbar(screenButton));

        InvokeHandleMouse(application, screenButton, MouseAction.Down);

        Assert.Equal(40, scroller.ScrollTop);
    }

    [Fact]
    public void PopupScrollbarInteractionMapsScrolledAncestorOffsets()
    {
        var window = new AppWindow("popup-scrolled-ancestor-scrollbar");
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var anchor = new Button { Geometry = new Rect(20, 10, 40, 20) };
        var popup = new Popup
        {
            Geometry = new Rect(0, 0, 100, 100),
            Anchor = anchor,
            VerticalOffset = 0
        };
        var ancestor = new View { Geometry = popup.Geometry };
        ancestor.Style.Set("overflow-y", "auto");
        ancestor.Style.Set("scrollbar-width", "none");
        ancestor.SetScrollContentSize(new Size(100, 300));
        ancestor.ScrollTop = 40;
        var scroller = new ScrollViewer { Geometry = new Rect(0, 20, 100, 100) };
        scroller.SetScrollContentSize(new Size(85, 300));
        ancestor.Children.Add(scroller);
        popup.Children.Add(ancestor);
        root.Children.Add(anchor);
        root.Children.Add(popup);
        window.Load(root);
        popup.Open();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var localButton = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;
        var popupBounds = popup.PopupBounds;
        var screenButton = new Point(
            localButton.X + popupBounds.X - popup.Geometry.X,
            localButton.Y - ancestor.ScrollTop + popupBounds.Y - popup.Geometry.Y);
        Assert.Same(scroller, tree.HitTestScrollbar(screenButton));

        InvokeHandleMouse(application, screenButton, MouseAction.Down);

        Assert.Equal(40, scroller.ScrollTop);
    }

    [Fact]
    public void PopupOverlayOccludesScrollbarInsidePopupSubtree()
    {
        var window = new AppWindow("popup-scrollbar-occlusion");
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var anchor = new Button { Geometry = new Rect(20, 10, 40, 20) };
        var popup = new Popup
        {
            Geometry = new Rect(0, 0, 100, 100),
            Anchor = anchor,
            VerticalOffset = 0
        };
        var scroller = new ScrollViewer { Geometry = popup.Geometry };
        scroller.SetScrollContentSize(new Size(85, 300));
        var overlay = new View { Geometry = new Rect(80, 0, 20, 100), ZIndex = 1 };
        popup.Children.Add(scroller);
        popup.Children.Add(overlay);
        root.Children.Add(anchor);
        root.Children.Add(popup);
        window.Load(root);
        popup.Open();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var localButton = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;
        var popupBounds = popup.PopupBounds;
        var screenButton = new Point(
            localButton.X + popupBounds.X - popup.Geometry.X,
            localButton.Y + popupBounds.Y - popup.Geometry.Y);

        InvokeHandleMouse(application, screenButton, MouseAction.Down);

        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void LaterEqualZPopupSiblingOccludesEarlierScrollbar()
    {
        var window = new AppWindow("popup-scrollbar-equal-z-occlusion");
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var anchor = new Button { Geometry = new Rect(20, 10, 40, 20) };
        var popup = new Popup
        {
            Geometry = new Rect(0, 0, 100, 100),
            Anchor = anchor,
            VerticalOffset = 0
        };
        var scroller = new ScrollViewer { Geometry = popup.Geometry };
        scroller.SetScrollContentSize(new Size(85, 300));
        var overlay = new View { Geometry = new Rect(80, 0, 20, 100) };
        popup.Children.Add(scroller);
        popup.Children.Add(overlay);
        root.Children.Add(anchor);
        root.Children.Add(popup);
        window.Load(root);
        popup.Open();
        var tree = new DisplayTree();
        tree.Synchronize(root);
        var localButton = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;
        var popupBounds = popup.PopupBounds;
        var screenButton = new Point(
            localButton.X + popupBounds.X - popup.Geometry.X,
            localButton.Y + popupBounds.Y - popup.Geometry.Y);

        Assert.Null(tree.HitTestScrollbar(screenButton));
        Assert.Same(overlay, tree.HitTestPopups(screenButton));
    }

    [Fact]
    public void FixedOverlayOccludesUnderlyingScrollbarInteraction()
    {
        var window = new AppWindow("fixed-scrollbar-occlusion");
        var root = new View { Geometry = new Rect(0, 0, 100, 100) };
        var scroller = new ScrollViewer { Geometry = root.Geometry };
        scroller.SetScrollContentSize(new Size(85, 300));
        var overlay = new View { Geometry = new Rect(80, 0, 20, 100) };
        overlay.Style.Set("position", "fixed");
        root.Children.Add(scroller);
        root.Children.Add(overlay);
        window.Load(root);
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var button = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;

        InvokeHandleMouse(application, button, MouseAction.Down);

        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void NormalOverlayOccludesUnderlyingScrollbarInteraction()
    {
        var window = new AppWindow("normal-scrollbar-occlusion");
        var root = new View { Geometry = new Rect(0, 0, 100, 100) };
        var scroller = new ScrollViewer { Geometry = root.Geometry };
        scroller.SetScrollContentSize(new Size(85, 300));
        var overlay = new View { Geometry = new Rect(80, 0, 20, 100), ZIndex = 1 };
        root.Children.Add(scroller);
        root.Children.Add(overlay);
        window.Load(root);
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var button = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;

        InvokeHandleMouse(application, button, MouseAction.Down);

        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void ModalBackdropOccludesUnderlyingScrollbarInteraction()
    {
        var window = new AppWindow("modal-scrollbar-occlusion");
        var document = Assert.IsType<UIDocument>(window.Document);
        document.Ui.Geometry = new Rect(0, 0, 100, 100);
        document.Body.Geometry = document.Ui.Geometry;
        var root = new View { Geometry = document.Ui.Geometry };
        var scroller = new ScrollViewer { Geometry = root.Geometry };
        scroller.SetScrollContentSize(new Size(85, 300));
        var dialog = new Dialog { Geometry = new Rect(0, 0, 40, 40) };
        root.Children.Add(scroller);
        root.Children.Add(dialog);
        window.Load(root);
        dialog.Open();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var button = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;
        Assert.Same(dialog, tree.HitTestPopups(button));

        InvokeHandleMouse(application, button, MouseAction.Down);

        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void FixedScrollbarUnderScrolledAncestorUsesFixedCoordinates()
    {
        var window = new AppWindow("fixed-scrollbar-mapping");
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var ancestor = new View { Geometry = new Rect(0, 0, 100, 100) };
        ancestor.Style.Set("overflow-y", "auto");
        ancestor.Style.Set("scrollbar-width", "none");
        ancestor.SetScrollContentSize(new Size(100, 300));
        ancestor.ScrollTop = 40;
        var fixedRoot = new View { Geometry = new Rect(0, 0, 100, 100) };
        fixedRoot.Style.Set("position", "fixed");
        var scroller = new ScrollViewer { Geometry = fixedRoot.Geometry };
        scroller.SetScrollContentSize(new Size(85, 300));
        fixedRoot.Children.Add(scroller);
        ancestor.Children.Add(fixedRoot);
        root.Children.Add(ancestor);
        window.Load(root);
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var button = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;

        Assert.Same(scroller, tree.HitTestScrollbar(button));
        InvokeHandleMouse(application, button, MouseAction.Down);

        Assert.Equal(40, scroller.ScrollTop);
    }

    [Fact]
    public void ScrollbarInsideScrolledFixedRootUsesFixedRootScrollMapping()
    {
        var window = new AppWindow("scrolled-fixed-root-scrollbar-mapping");
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var fixedRoot = new View { Geometry = new Rect(0, 0, 100, 100) };
        fixedRoot.Style.Set("position", "fixed");
        fixedRoot.Style.Set("overflow-y", "auto");
        fixedRoot.SetScrollContentSize(new Size(100, 300));
        fixedRoot.ScrollTop = 40;
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 80, 100) };
        scroller.SetScrollContentSize(new Size(65, 300));
        fixedRoot.Children.Add(scroller);
        root.Children.Add(fixedRoot);
        window.Load(root);
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var localButton = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;
        var screenButton = new Point(localButton.X, localButton.Y - fixedRoot.ScrollTop);

        Assert.Same(scroller, tree.HitTestScrollbar(screenButton));
        InvokeHandleMouse(application, screenButton, MouseAction.Down);

        Assert.Equal(40, scroller.ScrollTop);
    }

    [Fact]
    public void HiddenScrollbarCaptureIsReleasedBeforePointerRouting()
    {
        var (application, scroller) = CreateApplication();
        var thumb = scroller.GetScrollbarMetrics().VerticalThumb.Center;

        InvokeHandleMouse(application, thumb, MouseAction.Down);
        Assert.Same(scroller, GetPrivateField<Element>(application, "_draggingScrollbar"));

        scroller.IsVisible = false;
        InvokeHandleMouse(application, new Point(500, 500), MouseAction.Move, MouseButton.None);

        Assert.Null(GetPrivateField<Element>(application, "_draggingScrollbar"));
    }

    [Fact]
    public void HiddenPressedScrollbarStopsRepeatingOnTick()
    {
        var (application, scroller) = CreateApplication();
        var button = scroller.GetScrollbarMetrics().VerticalForwardButton.Center;

        InvokeHandleMouse(application, button, MouseAction.Down);
        Assert.Equal(40, scroller.ScrollTop);

        scroller.IsVisible = false;
        SetPrivateField(application, "_nextScrollbarRepeatSeconds", 0d);
        InvokeHandleTick(application);

        Assert.Equal(40, scroller.ScrollTop);
        Assert.Null(GetPrivateField<Element>(application, "_pressedScrollbar"));
    }

    [Fact]
    public void DisplayNoneScrollbarCaptureIsReleasedOnTick()
    {
        var (application, scroller) = CreateApplication();
        var metrics = scroller.GetScrollbarMetrics();
        InvokeHandleMouse(application, metrics.VerticalThumb.Center, MouseAction.Down);
        scroller.Style.Set("display", "none");

        InvokeHandleTick(application);

        Assert.Null(GetPrivateField<Element>(application, "_draggingScrollbar"));
        Assert.Null(GetPrivateField<Element>(application, "_pressedScrollbar"));
    }

    [Fact]
    public void GenericTextAreaScrollbarThumbStillDragsThroughElementCapture()
    {
        var (application, textArea) = CreateTextAreaApplication();
        var metrics = textArea.GetScrollbarMetrics();

        InvokeHandleMouse(application, metrics.VerticalThumb.Center, MouseAction.Down);
        InvokeHandleMouse(application, new Point(metrics.VerticalThumb.Center.X, metrics.VerticalTrack.Bottom), MouseAction.Move);

        Assert.True(textArea.ScrollTop > 0);
        InvokeHandleMouse(application, metrics.VerticalThumb.Center, MouseAction.Up);
    }

    [Fact]
    public void GenericTextAreaScrollbarButtonStillRepeatsThroughElementCapture()
    {
        var (application, textArea) = CreateTextAreaApplication();
        var button = textArea.GetScrollbarMetrics().VerticalForwardButton.Center;

        InvokeHandleMouse(application, button, MouseAction.Down);
        var afterPress = textArea.ScrollTop;
        SetPrivateField(application, "_nextScrollbarRepeatSeconds", 0d);
        InvokeHandleTick(application);

        Assert.True(textArea.ScrollTop > afterPress);
        InvokeHandleMouse(application, button, MouseAction.Up);
    }

    [Fact]
    public void SwitchingToMobileReleasesDesktopScrollbarCapture()
    {
        var window = new AppWindow("switch-scrollbar-profile-capture")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Desktop
        };
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        window.Load(scroller);
        ((IComponentLifecycle)scroller).OnAttached();
        scroller.SetScrollContentSize(new Size(85, 400));
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(scroller);
        var thumb = scroller.GetScrollbarMetrics().VerticalThumb.Center;

        InvokeHandleMouse(application, thumb, MouseAction.Down);
        Assert.Same(scroller, GetPrivateField<Element>(application, "_draggingScrollbar"));

        window.ScrollbarProfile = ScrollbarDeviceProfile.Mobile;
        InvokeHandleMouse(application, new Point(500, 500), MouseAction.Move, MouseButton.None);

        Assert.Null(GetPrivateField<Element>(application, "_draggingScrollbar"));
    }

    [Fact]
    public void ClosingPopupReleasesScrollbarCapture()
    {
        var window = new AppWindow("close-popup-scrollbar-capture");
        var root = new View { Geometry = new Rect(0, 0, 300, 200) };
        var anchor = new Button { Geometry = new Rect(220, 10, 40, 20) };
        var popup = new Popup
        {
            Geometry = new Rect(0, 0, 100, 100),
            Anchor = anchor
        };
        var scroller = new ScrollViewer { Geometry = popup.Geometry };
        scroller.SetScrollContentSize(new Size(85, 300));
        popup.Children.Add(scroller);
        root.Children.Add(anchor);
        root.Children.Add(popup);
        window.Load(root);
        popup.Open();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(root);
        var thumb = scroller.GetScrollbarMetrics().VerticalThumb.Center;
        var popupBounds = popup.PopupBounds;
        var screenThumb = new Point(
            thumb.X + popupBounds.X - popup.Geometry.X,
            thumb.Y + popupBounds.Y - popup.Geometry.Y);

        InvokeHandleMouse(application, screenThumb, MouseAction.Down);
        Assert.Same(scroller, GetPrivateField<Element>(application, "_draggingScrollbar"));
        popup.Close();

        InvokeHandleMouse(application, new Point(500, 500), MouseAction.Move, MouseButton.None);

        Assert.Null(GetPrivateField<Element>(application, "_draggingScrollbar"));
        Assert.Equal(0, scroller.ScrollTop);
    }

    [Fact]
    public void MobileFadeRequestedBeforeAttachIsRescheduledOnAttach()
    {
        var window = new AppWindow("mobile-fade-before-attach")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Mobile
        };
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        scroller.Style.Set("overflow-y", "auto");
        window.Load(scroller);
        scroller.SetScrollContentSize(new Size(100, 300));
        scroller.ScrollTop = 50;
        var application = new DesktopApplication(window);
        scroller.AddEventListener(StandardEvents.RequestFrame, e => InvokeHandleFrameRequest(application, e));

        ((IComponentLifecycle)scroller).OnAttached();

        var scheduled = Assert.IsType<Dictionary<Element, double>>(
            GetPrivateField<Dictionary<Element, double>>(application, "_scheduledFrames"));
        Assert.True(scheduled.ContainsKey(scroller));
    }

    private static (DesktopApplication Application, ScrollViewer Scroller) CreateApplication()
    {
        var window = new AppWindow("scrollbar-host")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Desktop
        };
        var scroller = new ScrollViewer { Geometry = new Rect(0, 0, 100, 100) };
        window.Load(scroller);
        ((IComponentLifecycle)scroller).OnAttached();
        scroller.SetScrollContentSize(new Size(85, 400));
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(scroller);
        return (application, scroller);
    }

    private static (DesktopApplication Application, TextArea TextArea) CreateTextAreaApplication()
    {
        var window = new AppWindow("textarea-scrollbar-host")
        {
            ScrollbarProfile = ScrollbarDeviceProfile.Desktop
        };
        var textArea = new TextArea { Geometry = new Rect(0, 0, 100, 100) };
        textArea.Style.Set("overflow-y", "auto");
        textArea.SetScrollContentSize(new Size(100, 400));
        window.Load(textArea);
        ((IComponentLifecycle)textArea).OnAttached();
        var application = new DesktopApplication(window);
        SetPrivateField(application, "_host", new TestHost());
        var tree = Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree"));
        tree.Synchronize(textArea);
        return (application, textArea);
    }

    private static void InvokeHandleMouse(
        DesktopApplication application,
        Point point,
        MouseAction action,
        MouseButton button = MouseButton.Left)
    {
        var method = typeof(DesktopApplication).GetMethod("HandleMouse", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, [point, action, button]);
    }

    private static void InvokeHandleTick(DesktopApplication application)
    {
        var method = typeof(DesktopApplication).GetMethod("HandleTick", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, null);
    }

    private static void InvokeUpdateScrollbarHover(DesktopApplication application, Point point)
    {
        var method = typeof(DesktopApplication).GetMethod(
            "UpdateScrollbarHover",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, [point]);
    }

    private static void InvokeHandleFrameRequest(DesktopApplication application, Event request)
    {
        var method = typeof(DesktopApplication).GetMethod(
            "HandleFrameRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, [request]);
    }

    private static void SetPrivateField<T>(DesktopApplication application, string name, T value)
    {
        var field = typeof(DesktopApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(application, value);
    }

    private static T? GetPrivateField<T>(DesktopApplication application, string name)
    {
        var field = typeof(DesktopApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T?)field!.GetValue(application);
    }

    private sealed class TestHost : IPlatformHost
    {
        public Size ClientSize => new(100, 100);
        public float DpiScale => 1;
        public bool IsRunning => true;
        public string Title { get; set; } = "scrollbar-host";
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers => KeyModifiers.None;
        public event Action<Size>? SizeChanged { add { } remove { } }
        public event Action<Point, MouseAction, MouseButton>? MouseEvent { add { } remove { } }
        public event Action<Point, int>? WheelEvent { add { } remove { } }
        public event Action<int, KeyAction>? KeyEvent { add { } remove { } }
        public event Action<string>? TextInput { add { } remove { } }
        public event Action? Tick { add { } remove { } }
        public void Show() { }
        public void Close() { }
        public IRenderContext CreateRenderContext() => throw new NotSupportedException();
        public void PumpEvents() { }
        public void SetTextInputRect(Rect rect) { }
        public string GetClipboardText() => "";
        public void SetClipboardText(string text) { }
        public void Dispose() { }
    }

    private sealed class NonSchedulingScrollViewer : ScrollViewer
    {
        public override void InvalidatePaint() { }
    }

    private sealed class FrameCountingScrollViewer : ScrollViewer
    {
        public int FrameDueCount { get; private set; }

        protected override void OnFrameDueCore()
        {
            FrameDueCount++;
            base.OnFrameDueCore();
        }
    }
}
