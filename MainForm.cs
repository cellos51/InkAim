using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net;
using System.Numerics;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace OctoPoint
{
    public partial class MainForm : Form
    {
        //Vector2 lastMousePos = new Vector2(0,0);
        Vector2 idlePos = new Vector2(Screen.PrimaryScreen.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2);
        Vector3 rotation = new Vector3(0, 0, 0);

        private static UDPServer server;

        [DllImport("user32.dll")]
        static extern bool SetSystemCursor(IntPtr handle, uint id);
        [DllImport("user32.dll")]
        static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
        [DllImport("user32.dll")]
        static extern IntPtr LoadCursorFromFile(string lpFileName);
        [DllImport("user32.dll")]
        static extern IntPtr CopyIcon(IntPtr hIcon);
        [DllImport("user32.dll")]
        static extern bool DestroyCursor(IntPtr hCursor);

        //Normal cursor
        private static uint OCR_NORMAL = 32512;

        static IntPtr oldCursor = CopyIcon(LoadCursor(IntPtr.Zero, (int)OCR_NORMAL));

        private static bool cursorHidden = false;

        private static System.IO.MemoryStream cursorMemoryStream = new System.IO.MemoryStream(InkAim.Properties.Resources.invisible);
        private static System.Windows.Forms.Cursor newCursor = new System.Windows.Forms.Cursor(cursorMemoryStream);

        public static void HideCursorGlobal()
        {
            if (cursorHidden == false)
            {
                IntPtr invis = CopyIcon(newCursor.Handle);
                SetSystemCursor(invis, OCR_NORMAL);
                DestroyCursor(invis);
                cursorHidden = true;         
            }
        }

        public static void ShowCursorGlobal()
        {
            if (cursorHidden == true)
            {
                SetSystemCursor(oldCursor, OCR_NORMAL);
                DestroyCursor(oldCursor);
                oldCursor = CopyIcon(LoadCursor(IntPtr.Zero, (int)OCR_NORMAL));
                cursorHidden = false;
            }
        }

        public MainForm()
        {
            InitializeComponent();
            server = new UDPServer();
            server.Start(IPAddress.Parse("127.0.0.1"), 26760);
            System.Diagnostics.Debug.WriteLine("Server initialized");

            AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);
            Timer tmr = new Timer();
            tmr.Interval = 1;   // milliseconds
            tmr.Tick += Tmr_Tick;  // set handler
            tmr.Start();
        }

        private void Tmr_Tick(object sender, EventArgs e)  //run this logic each timer tick
        {
            Vector3 gyro = new Vector3(0,0,0);
            Vector2 mouseGyro = new Vector2(-(System.Windows.Forms.Cursor.Position.Y - idlePos.Y), (System.Windows.Forms.Cursor.Position.X - idlePos.X));

            

            if (!Keyboard.IsKeyDown(Key.Tab) && Keyboard.IsKeyToggled(Key.F1))
            {
                HideCursorGlobal();
                if (((rotation.X + mouseGyro.X) / 63) < 80 && ((rotation.X + mouseGyro.X) / 63) > -80)
                {
                    gyro += new Vector3(mouseGyro.X, 0, 0);
                    rotation += gyro;
                }

                System.Windows.Forms.Cursor.Position = new Point((int)idlePos.X, (int)idlePos.Y);


                gyro += new Vector3(0, (mouseGyro.Y * (1 - ((server.currentRotation.X / 63) / 90f))), -(mouseGyro.Y * ((server.currentRotation.X / 63) / 90f)) );

                server.sendMotionData(gyro * ((float)this.trackBar1.Value / 10), (MouseButtons & MouseButtons.Left) == MouseButtons.Left, (MouseButtons & MouseButtons.Right) == MouseButtons.Right);
            }
            else 
            {
                ShowCursorGlobal();
                server.sendMotionData(new Vector3(0,0,0), (MouseButtons & MouseButtons.Left) == MouseButtons.Left, (MouseButtons & MouseButtons.Right) == MouseButtons.Right);
            }  
        }

        static void OnProcessExit(object sender, EventArgs e)
        {
            ShowCursorGlobal();
            DestroyCursor(oldCursor);
        }
    }
}
