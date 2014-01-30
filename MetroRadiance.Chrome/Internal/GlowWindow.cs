﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using MetroRadiance.Chrome.Behaviors;
using MetroRadiance.Core;
using MetroRadiance.Core.Win32;

namespace MetroRadiance.Chrome.Internal
{
	/// <summary>
	/// ウィンドウの四辺にアタッチされる発光ウィンドウを表します。
	/// </summary>
	internal class GlowWindow : Window
	{
		static GlowWindow()
		{
			DefaultStyleKeyProperty.OverrideMetadata(typeof(GlowWindow), new FrameworkPropertyMetadata(typeof(GlowWindow)));
		}

		private static Dpi? systemDpi;

		private IntPtr handle;
		private readonly GlowWindowProcessor processor;
		private bool closed;

		private readonly Window owner;
		private IntPtr ownerHandle;

		#region IsGlowing 依存関係プロパティ

		public bool IsGlowing
		{
			get { return (bool)this.GetValue(IsGlowingProperty); }
			set { this.SetValue(IsGlowingProperty, value); }
		}

		public static readonly DependencyProperty IsGlowingProperty =
			DependencyProperty.Register("IsGlowing", typeof(bool), typeof(GlowWindow), new UIPropertyMetadata(true));

		#endregion

		#region Orientation 依存関係プロパティ

		public Orientation Orientation
		{
			get { return (Orientation)this.GetValue(OrientationProperty); }
			set { this.SetValue(OrientationProperty, value); }
		}

		public static readonly DependencyProperty OrientationProperty =
			DependencyProperty.Register("Orientation", typeof(Orientation), typeof(GlowWindow), new UIPropertyMetadata(Orientation.Horizontal));

		#endregion

		#region ActiveGlowBrush 依存関係プロパティ

		public Brush ActiveGlowBrush
		{
			get { return (Brush)this.GetValue(ActiveGlowBrushProperty); }
			set { this.SetValue(ActiveGlowBrushProperty, value); }
		}

		public static readonly DependencyProperty ActiveGlowBrushProperty =
			DependencyProperty.Register("ActiveGlowBrush", typeof(Brush), typeof(GlowWindow), new UIPropertyMetadata(null));

		#endregion

		#region InactiveGlowBrush 依存関係プロパティ

		public Brush InactiveGlowBrush
		{
			get { return (Brush)this.GetValue(InactiveGlowBrushProperty); }
			set { this.SetValue(InactiveGlowBrushProperty, value); }
		}

		public static readonly DependencyProperty InactiveGlowBrushProperty =
			DependencyProperty.Register("InactiveGlowBrush", typeof(Brush), typeof(GlowWindow), new UIPropertyMetadata(null));

		#endregion

		#region ChromeMode 依存関係プロパティ

		public ChromeMode ChromeMode
		{
			get { return (ChromeMode)this.GetValue(ChromeModeProperty); }
			set { this.SetValue(ChromeModeProperty, value); }
		}
		public static readonly DependencyProperty ChromeModeProperty =
			DependencyProperty.Register("ChromeMode", typeof(ChromeMode), typeof(GlowWindow), new UIPropertyMetadata(ChromeMode.VisualStudio2013));

		#endregion

		internal GlowWindow(MetroChromeBehavior behavior, GlowWindowProcessor processor)
		{
			this.owner = behavior.Window;
			this.processor = processor;

			this.WindowStyle = WindowStyle.None;
			this.AllowsTransparency = true;
			this.ShowActivated = false;
			this.Visibility = Visibility.Collapsed;
			this.ResizeMode = ResizeMode.NoResize;
			this.WindowStartupLocation = WindowStartupLocation.Manual;
			this.Left = processor.GetLeft(this.owner.Left, this.owner.ActualWidth);
			this.Top = processor.GetTop(this.owner.Top, this.owner.ActualHeight);
			this.Width = processor.GetWidth(this.owner.Left, this.owner.ActualWidth);
			this.Height = processor.GetHeight(this.owner.Top, this.owner.ActualHeight);
			this.Orientation = processor.Orientation;
			this.HorizontalContentAlignment = processor.HorizontalAlignment;
			this.VerticalContentAlignment = processor.VerticalAlignment;

			var bindingActive = new Binding("ActiveBrush") { Source = behavior, };
			this.SetBinding(ActiveGlowBrushProperty, bindingActive);

			var bindingInactive = new Binding("InactiveBrush") { Source = behavior, };
			this.SetBinding(InactiveGlowBrushProperty, bindingInactive);

			var bindingChromeMode = new Binding("Mode") { Source = behavior, };
			this.SetBinding(ChromeModeProperty, bindingChromeMode);

			this.owner.ContentRendered += (sender, args) =>
			{
				this.Show();
				this.Update();
			};
			this.owner.Activated += (sender, args) => this.Update();
			this.owner.Deactivated += (sender, args) => this.Update();
			this.owner.SizeChanged += (sender, args) => this.Update();
			this.owner.LocationChanged += (sender, args) => this.Update();
			this.owner.StateChanged += (sender, args) => this.Update();
			this.owner.Closed += (sender, args) => this.Close();
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);

			var source = PresentationSource.FromVisual(this) as HwndSource;
			if (source != null)
			{
				this.handle = source.Handle;

				var wsex = this.handle.GetWindowLongEx();
				wsex ^= WSEX.APPWINDOW;
				wsex |= WSEX.NOACTIVATE;
				this.handle.SetWindowLongEx(wsex);

				var cs = this.handle.GetClassLong(ClassLongFlags.GclStyle);
				cs |= ClassStyles.DblClks;
				this.handle.SetClassLong(ClassLongFlags.GclStyle, cs);

				source.AddHook(this.WndProc);

				systemDpi = this.GetSystemDpi();
			}
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			var source = PresentationSource.FromVisual(this) as HwndSource;
			if (source != null)
			{
				source.RemoveHook(this.WndProc);
			}
			this.closed = true;
		}

		public void Update()
		{
			if (closed) return;

			if (this.owner.Visibility == Visibility.Hidden)
			{
				this.Visibility = Visibility.Hidden;
				this.UpdateCore();
			}
			else if (this.owner.WindowState == WindowState.Normal)
			{
				this.Visibility = Visibility.Visible;
				this.UpdateCore();
			}
			else
			{
				this.Visibility = Visibility.Collapsed;
			}
		}

		private void UpdateCore()
		{
			if (this.ownerHandle == IntPtr.Zero)
			{
				this.ownerHandle = new WindowInteropHelper(this.owner).Handle;
			}

			this.IsGlowing = this.owner.IsActive;

			var dpi = systemDpi ?? (systemDpi = this.GetSystemDpi()).Value;
			var left = (int)Math.Round(this.processor.GetLeft(this.owner.Left, this.owner.ActualWidth) * dpi.ScaleX);
			var top = (int)Math.Round(this.processor.GetTop(this.owner.Top, this.owner.ActualHeight) * dpi.ScaleY);
			var width = (int)Math.Round(this.processor.GetWidth(this.owner.Left, this.owner.ActualWidth) * dpi.ScaleX);
			var height = (int)Math.Round(this.processor.GetHeight(this.owner.Top, this.owner.ActualHeight) * dpi.ScaleY);
			NativeMethods.SetWindowPos(this.handle, this.ownerHandle, left, top, width, height, SWP.NOACTIVATE);
		}


		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == (int)WM.MOUSEACTIVATE)
			{
				handled = true;
				return new IntPtr(3);
			}

			if (msg == (int)WM.LBUTTONDOWN)
			{
				if (!this.owner.IsActive)
				{
					this.owner.Activate();
				}

				var ptScreen = lParam.ToPoint();

				NativeMethods.PostMessage(
					this.ownerHandle,
					(uint)WM.NCLBUTTONDOWN,
					(IntPtr)this.processor.GetHitTestValue(ptScreen, this.ActualWidth, this.ActualHeight),
					IntPtr.Zero);
			}
			if (msg == (int)WM.NCHITTEST)
			{
				var ptScreen = lParam.ToPoint();
				var ptClient = this.PointFromScreen(ptScreen);

				this.Cursor = this.processor.GetCursor(ptClient, this.ActualWidth, this.ActualHeight);
			}

			if (msg == (int)WM.LBUTTONDBLCLK)
			{
				if (this.processor.GetType() == typeof(GlowWindowProcessorTop))
				{
					NativeMethods.SendMessage(this.ownerHandle, WM.NCLBUTTONDBLCLK, (IntPtr)HitTestValues.HTTOP, IntPtr.Zero);
				}
				else if (this.processor.GetType() == typeof(GlowWindowProcessorBottom))
				{
					NativeMethods.SendMessage(this.ownerHandle, WM.NCLBUTTONDBLCLK, (IntPtr)HitTestValues.HTBOTTOM, IntPtr.Zero);
				}
			}

			if (msg == (int)WM.DPICHANGED)
			{
				systemDpi = this.GetSystemDpi();
			}

			return IntPtr.Zero;
		}
	}
}
