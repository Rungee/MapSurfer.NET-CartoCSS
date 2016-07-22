using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Permissions;

using MapSurfer.Windows.Forms;

namespace MapSurfer.Styling.Formats.CartoCSS.Export
{
	internal class CartoExportFileDialog : Disposable, ISaveFileDialog
	{
		public class WindowWrapper : IWin32Window
		{
			public WindowWrapper(IntPtr handle)
			{
				_hwnd = handle;
			}

			public IntPtr Handle
			{
				get { return _hwnd; }
			}

			private IntPtr _hwnd;
		}
		
		private IntPtr m_comboFormatHandle;

		private string m_filter = string.Empty;
		private string m_defaultExt = string.Empty;
		private string m_fileName = string.Empty;
		private string m_title = string.Empty;
		private int m_filterIndex = -1;
		private bool m_overwritePrompt = true;
		private bool m_addExtension = true;

		private string m_format = "MapSurfer.NET";
		private string[] m_formats;
		private Screen m_activeScreen;

		private const int CDN_FILEOK = -606;
		
		public CartoExportFileDialog(string[] formats)
		{
			m_formats = formats;
			m_format = formats[0];
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				// TODO
			}

			base.Dispose(disposing);
		}

		public string Format
		{
			get { return m_format; }
			set { m_format = value; }
		}

		public string Filter
		{
			get
			{
				return m_filter;
			}
			set
			{
				m_filter = value;
			}
		}

		public string FileName
		{
			get
			{
				return m_fileName;
			}
			set
			{
				m_fileName = value;
			}
		}

		public string Title
		{
			get
			{
				return m_title;
			}
			set
			{
				m_title = value;
			}
		}

		public bool OverwritePrompt
		{
			get
			{
				return m_overwritePrompt;
			}
			set
			{
				m_overwritePrompt = value;
			}
		}

		public bool AddExtension
		{
			get
			{
				return m_addExtension;
			}
			set
			{
				m_addExtension = value;
			}
		}

		public bool CheckPathExists { get; set; }

		public string DefaultExtension
		{
			get
			{
				return m_defaultExt;
			}
			set
			{
				m_defaultExt = value;
			}
		}

		public bool DereferenceLinks { get; set; }

		public int FilterIndex {
			get {
				return m_filterIndex;
			}
			set
			{
				m_filterIndex = value;
			}
		}

		public string InitialDirectory { get; set; }

		public bool RestoreDirectory { get; set; }

    [SecurityCritical]
    [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.UnmanagedCode)]
		private int HookProc(IntPtr hdlg, int msg, int wParam, int lParam)
		{
			switch ((NativeEnums.WindowMessages)msg)
			{
				case NativeEnums.WindowMessages.WM_INITDIALOG:
					Rectangle rcScreen = m_activeScreen.Bounds;
					NativeStructs.RECT rcDialog = new NativeStructs.RECT();
					IntPtr parent = NativeMethods.GetParent(hdlg);
					NativeMethods.GetWindowRect(parent, ref rcDialog);

					IntPtr fileComboWindow = NativeMethods.GetDlgItem(parent, 0x470);
          		
          for (int i = 0; i < m_formats.Length; i++)
            NativeMethods.SendMessage(fileComboWindow, (int)NativeEnums.ComboboxControlMessages.CB_ADDSTRING, 0, m_formats[i]);
          NativeMethods.SendMessage(fileComboWindow, (int)NativeEnums.ComboboxControlMessages.CB_SETCURSEL, 0, 0);

          m_comboFormatHandle = fileComboWindow;//CreateComboBox("combobox_builder", fontHandle, m_formats, 2, point.x, point.y + 8, aboveRect.right - point.x, 100, parent);
          break;
				case NativeEnums.WindowMessages.WM_DESTROY:
					break;
				case NativeEnums.WindowMessages.WM_NOTIFY:
          // The following line throws unknown exception
          //	NativeStructs.OFNotify notify = (NativeStructs.OFNotify)NativeMethods.PtrToStructure(new IntPtr(lParam), typeof(NativeStructs.OFNotify));

          //if (notify.hdr.code == CDN_FILEOK)
          if (m_comboFormatHandle != IntPtr.Zero)
          {
						int index = NativeMethods.SendMessage(m_comboFormatHandle, (int)NativeEnums.ComboboxControlMessages.CB_GETCURSEL, 0, 0);
						m_format = m_formats[index];
					}
					break;

			}
			return 0;
		}

		private void DestroyWindow(ref IntPtr handle)
		{
			if (handle != IntPtr.Zero)
			{
				NativeMethods.DestroyWindow(handle);
				handle = IntPtr.Zero;
			}
		}

		private IntPtr CreateComboBox(string name, int fontHandle, string[] items, int selectedIndex, int x, int y, int width, int height, IntPtr parent)
		{
			IntPtr comboHandle = NativeMethods.CreateWindowEx(NativeEnums.WindowStylesEx.WS_EX_LEFT, "ComboBox", name, (int)NativeEnums.WindowStyles.WS_VISIBLE | (int)NativeEnums.WindowStyles.WS_CHILD | (int)NativeEnums.ComboboxControlStyles.CBS_HASSTRINGS | (int)NativeEnums.ComboboxControlStyles.CBS_DROPDOWNLIST | (int)NativeEnums.WindowStyles.WS_TABSTOP, x, y, width, height, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
			NativeMethods.SendMessage(comboHandle, (int)NativeEnums.WindowMessages.WM_SETFONT, fontHandle, 0);

			for (int i = 0; i < items.Length; i++)
			{
				NativeMethods.SendMessage(comboHandle, (int)NativeEnums.ComboboxControlMessages.CB_ADDSTRING, 0, items[i]);
			}

			NativeMethods.SendMessage(comboHandle, (int)NativeEnums.ComboboxControlMessages.CB_SETCURSEL, selectedIndex, 0);

			return comboHandle;
		}

		public DialogResult ShowDialog()
		{
			WindowWrapper wrap = new WindowWrapper(System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle);
			return this.ShowDialog(wrap);
		}

		public DialogResult ShowDialog(IWin32Window window)
		{
			NativeStructs.OPENFILENAME ofn = new NativeStructs.OPENFILENAME();

      try
      {
        ofn.lStructSize = Marshal.SizeOf(ofn);
        ofn.lpstrFilter = m_filter.Replace('|', '\0') + '\0';

        ofn.lpstrFile = m_fileName + new string(' ', 512);
        ofn.nMaxFile = ofn.lpstrFile.Length;
        ofn.lpstrFileTitle = System.IO.Path.GetFileName(m_fileName) + new string(' ', 512);
        ofn.nMaxFileTitle = ofn.lpstrFileTitle.Length;
        ofn.lpstrTitle = m_title;
        if (AddExtension)
          ofn.lpstrDefExt = m_defaultExt;

        ofn.hwndOwner = window.Handle;

        m_activeScreen = Screen.FromHandle(window.Handle);

        int flags = (int)(NativeEnums.OpenFileNameFlags.OFN_EXPLORER | NativeEnums.OpenFileNameFlags.OFN_PATHMUSTEXIST | NativeEnums.OpenFileNameFlags.OFN_NOTESTFILECREATE | NativeEnums.OpenFileNameFlags.OFN_ENABLEHOOK | NativeEnums.OpenFileNameFlags.OFN_HIDEREADONLY);
        if (m_overwritePrompt)
          flags |= (int)NativeEnums.OpenFileNameFlags.OFN_OVERWRITEPROMPT;

        ofn.Flags = flags;

        //set up hook
        ofn.lpfnHook = new NativeDelegates.OpenFileNameHookProc(HookProc);

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
          ofn.lStructSize -= 12;
        }

        if (!NativeMethods.GetSaveFileName(ofn))
        {
          int ret = NativeMethods.CommDlgExtendedError();

          if (ret != 0)
          {
            throw new ApplicationException("Couldn't show file open dialog - " + ret.ToString());
          }

          return DialogResult.Cancel;
        }

        m_fileName = ofn.lpstrFile;
      }
      finally
      {
       /* if (ofn.lpstrFile != IntPtr.Zero)
          Marshal.FreeCoTaskMem(ofn.lpstrFile);*/
      }

			return DialogResult.OK;
		}
	}
}
