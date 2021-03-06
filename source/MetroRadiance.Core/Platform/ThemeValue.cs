﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Media;
using MetroRadiance.Interop.Win32;
using MetroRadiance.Media;
using MetroRadiance.Utilities;
using Microsoft.Win32;

namespace MetroRadiance.Platform
{
	public abstract class WindowsThemeValue<T>
	{
		private event EventHandler<T> _changedEvent;
		private readonly HashSet<EventHandler<T>> _handlers = new HashSet<EventHandler<T>>();
		private ListenerWindow _listenerWindow;
		private T _current;
		private bool _hasValidValue;

		/// <summary>
		/// 現在の設定値を取得します。
		/// </summary>
		public T Current
		{
			get
			{
				if (!this._hasValidValue)
				{
					this._current = this.GetValue();
					this._hasValidValue = true;
				}

				return this._current;
			}
			set
			{
				this._current = value;
				this._hasValidValue = true;
			}
		}

		/// <summary>
		/// テーマ設定が変更されると発生します。
		/// </summary>
		public event EventHandler<T> Changed
		{
			add { this.Add(value); }
			remove { this.Remove(value); }
		}

		/// <summary>
		/// テーマ設定が変更されたときに通知を受け取るメソッドを登録します。
		/// </summary>
		/// <param name="callback">テーマ設定が変更されたときに通知を受け取るメソッド。</param>
		/// <returns>通知の購読を解除するときに使用する <see cref="IDisposable"/> オブジェクト。</returns>

		[EditorBrowsable(EditorBrowsableState.Never)]
		public IDisposable RegisterListener(Action<T> callback)
		{
			EventHandler<T> handler = (sender, e) => callback?.Invoke(e);
			this.Changed += handler;

			return Disposable.Create(() => this.Changed -= handler);
		}

		private void Add(EventHandler<T> listener)
		{
			if (this._handlers.Add(listener))
			{
				this._changedEvent += listener;

				if (this._listenerWindow == null)
				{
					this._listenerWindow = new ListenerWindow(this.GetType().Name, this.WndProc);
					this._listenerWindow.Show();
				}
			}
		}

		private void Remove(EventHandler<T> listener)
		{
			if (this._handlers.Remove(listener))
			{
				this._changedEvent -= listener;

				if (this._handlers.Count == 0)
				{
					this._listenerWindow?.Close();
					this._listenerWindow = null;
				}
			}
		}

		internal void Update(T data)
		{
			this.Current = data;
			this._changedEvent?.Invoke(this, data);
		}

		internal abstract T GetValue();

		internal abstract IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled);

		private class ListenerWindow : TransparentWindow
		{
			private readonly HwndSourceHook _hook;

			public ListenerWindow(string name, HwndSourceHook hook)
			{
				this.Name = $"{name} listener window";
				this._hook = hook;
			}

			protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
			{
				var result = this._hook(hwnd, msg, wParam, lParam, ref handled);
				return handled ? result : base.WndProc(hwnd, msg, wParam, lParam, ref handled);
			}
		}
	}

	public class AccentValue : WindowsThemeValue<Color>
	{
		internal override Color GetValue()
		{
			const string keyName = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\DWM";
			const string valueName = "ColorizationColor";
			int color;

			var colorizationColor = Registry.GetValue(keyName, valueName, null) as int?;
			if (colorizationColor != null)
			{
				color = colorizationColor.Value;
			}
			else
			{
				bool opaque;
				Dwmapi.DwmGetColorizationColor(out color, out opaque);
			}

			return ColorHelper.GetColorFromInt64(color);
		}

		internal override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == (int)WindowsMessages.WM_DWMCOLORIZATIONCOLORCHANGED)
			{
				var color = ColorHelper.GetColorFromInt64((long)wParam);
				this.Update(color);
				handled = true;
			}

			return IntPtr.Zero;
		}
	}

	public enum Theme
	{
		Dark = 0,
		Light = 1,
	}

	public class ThemeValue : WindowsThemeValue<Theme>
	{
		internal override Theme GetValue()
		{
			const string keyName = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
			const string valueName = "AppsUseLightTheme";

			return Registry.GetValue(keyName, valueName, null) as int? == 0 ? Theme.Dark : Theme.Light;
		}

		internal override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == (int)WindowsMessages.WM_SETTINGCHANGE)
			{
				var systemParmeter = Marshal.PtrToStringAuto(lParam);
				if (systemParmeter == "ImmersiveColorSet")
				{
					this.Update(this.GetValue());
					handled = true;
				}
			}

			return IntPtr.Zero;
		}
	}
}
