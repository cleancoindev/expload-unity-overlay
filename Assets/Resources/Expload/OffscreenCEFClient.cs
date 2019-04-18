﻿using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using UnityEngine;
using Xilium.CefGlue;

namespace Expload
{
    internal class OffscreenCEFClient : CefClient
    {
        private readonly OffscreenLoadHandler _loadHandler;
        private readonly OffscreenRenderHandler _renderHandler;
        private readonly object sPixelLock = new object();

        private byte[] sPixelBuffer;

        private CefBrowserHost sHost;

        public OffscreenCEFClient(int windowWidth, int windowHeight, bool hideScrollbars = false)
        {
            this._loadHandler = new OffscreenLoadHandler(this, hideScrollbars);
            this._renderHandler = new OffscreenRenderHandler(windowWidth, windowHeight, this);

            this.sPixelBuffer = new byte[windowWidth * windowHeight * 4];

            Debug.Log("Constructed Offscreen Client");
        }

        public void UpdateTexture(Texture2D pTexture)
        {
            lock (sPixelLock)
            {
                if (this.sHost == null)
                  return;

                pTexture.LoadRawTextureData(this.sPixelBuffer);
                pTexture.Apply(false);
            }
        }

        public void SendMouseMove(CefMouseEvent e)
        {
            if (this.sHost == null)
                return;

            this.sHost.SendMouseMoveEvent(e, false);
        }

        public void SendMouseClick(int x, int y, CefMouseButtonType button, bool mouseUp)
        {
            if (this.sHost == null)
                return;

            this.sHost.SendMouseClickEvent(new CefMouseEvent(x, y, 0), button, mouseUp, 1);
        }

        public void SendKey(CefKeyEvent e)
        {
            if (this.sHost == null)
                return;
            this.sHost.SendKeyEvent(e);
        }

        [SecurityCritical]
        public void Shutdown()
        {
            if (this.sHost == null)
                return;

            Debug.Log("Host Cleanup");
            var host = this.sHost;
            this.sHost = null;
            host.CloseBrowser(false);
            host.Dispose();
        }

        #region Interface

        protected override CefRenderHandler GetRenderHandler()
        {
            return this._renderHandler;
        }

        protected override CefLoadHandler GetLoadHandler()
        {
            return this._loadHandler;
        }

        #endregion Interface

        #region Handlers

        internal class OffscreenLoadHandler : CefLoadHandler
        {
            private OffscreenCEFClient client;
            private bool hideScrollbars;

            public OffscreenLoadHandler(OffscreenCEFClient client, bool hideScrollbars)
            {
                this.client = client;
                this.hideScrollbars = hideScrollbars;
            }

            protected override void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType)
            {
                if (browser != null)
                    this.client.sHost = browser.GetHost();

                if (frame.IsMain)
                    Debug.LogFormat("START: {0}", browser.GetMainFrame().Url);
            }

            protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode)
            {
                if (frame.IsMain)
                {
                    Debug.LogFormat("END: {0}, {1}", browser.GetMainFrame().Url, httpStatusCode.ToString());

                    if (this.hideScrollbars)
                        this.HideScrollbars(frame);
                }
            }

            private void HideScrollbars(CefFrame frame)
            {
                string jsScript = "var head = document.head;" +
                                  "var style = document.createElement('style');" +
                                  "style.type = 'text/css';" +
                                  "style.appendChild(document.createTextNode('::-webkit-scrollbar { visibility: hidden; }'));" +
                                  "head.appendChild(style);";
                frame.ExecuteJavaScript(jsScript, string.Empty, 107);
            }
        }

        internal class OffscreenRenderHandler : CefRenderHandler
        {
            private OffscreenCEFClient client;

            private readonly int _windowWidth;
            private readonly int _windowHeight;

            public OffscreenRenderHandler(int windowWidth, int windowHeight, OffscreenCEFClient client)
            {
                this._windowWidth = windowWidth;
                this._windowHeight = windowHeight;
                this.client = client;
            }

            protected override bool GetRootScreenRect(CefBrowser browser, ref CefRectangle rect)
            {
                return GetViewRect(browser, ref rect);
            }

            protected override bool GetScreenPoint(CefBrowser browser, int viewX, int viewY, ref int screenX, ref int screenY)
            {
                screenX = viewX;
                screenY = viewY;
                return true;
            }

            protected override bool GetViewRect(CefBrowser browser, ref CefRectangle rect)
            {
                rect.X = 0;
                rect.Y = 0;
                rect.Width = this._windowWidth;
                rect.Height = this._windowHeight;
                return true;
            }

            protected override void OnPopupShow(CefBrowser browser, bool show)
            {
                base.OnPopupShow(browser, show);
            }

//            private int drawCount = 0;

            [SecurityCritical]
            protected override void OnPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, IntPtr buffer, int width, int height)
            {
                //Debug.Log("drawCount=" + drawCount);
                //drawCount++;
                //if (type == CefPaintElementType.Popup)
                //{
                //    Debug.Log(dirtyRects.ToString());
                //    Debug.Log("w="+ width + ",h="+height);
                //    Debug.Log(dirtyRects.ToString());
                //    return;
                //}
                if (browser != null)
                {
                    lock (this.client.sPixelLock)
                    {
                        //  TODO Use dirtyRects
                        Marshal.Copy(buffer, this.client.sPixelBuffer, 0, this.client.sPixelBuffer.Length);
                    }
                }
            }

            protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo screenInfo)
            {
                return false;
            }

            protected override void OnCursorChange(CefBrowser browser, IntPtr cursorHandle, CefCursorType type, CefCursorInfo customCursorInfo)
            {
            }

            protected override void OnPopupSize(CefBrowser browser, CefRectangle rect)
            {
            }

            protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y)
            {
            }

            protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds)
            {
            }
        }

        #endregion Handlers

        public class OffscreenCEFApp : CefApp
        {
            protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine)
            {
                Console.WriteLine("OnBeforeCommandLineProcessing: {0} {1}", processType, commandLine);

                // TODO: currently on linux platform location of locales and pack files are determined
                // incorrectly (relative to main module instead of libcef.so module).
                // Once issue http://code.google.com/p/chromiumembedded/issues/detail?id=668 will be resolved this code can be removed.
                if (CefRuntime.Platform == CefRuntimePlatform.Linux)
                {
                    var path = new Uri(Assembly.GetEntryAssembly().CodeBase).LocalPath;
                    path = Path.GetDirectoryName(path);

                    commandLine.AppendSwitch("resources-dir-path", path);
                    commandLine.AppendSwitch("locales-dir-path", Path.Combine(path, "locales"));
                }
            }
        }
    }
}