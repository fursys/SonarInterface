using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using XBeeControl;
using System.Runtime.InteropServices;

namespace Sonar
{
    struct ServoPosStruct
    {
        public byte data_type;
        public UInt16 Status;
    }
    public partial class Form1 : Form
    {

        XBee xb;
        public const byte SERVO_POS = 0x01;
        public const byte SONAR_VALUE = 0x02;

        UInt16 ServoPos;
        double ServoPosGrad;
        double ServoPosRad;
        UInt16 SonarVal;
        Point center = new Point(600, 620);
        Pen DrawPen = new Pen(Color.Brown);
        Point[] SonarArray = new Point[60];
        double[] SonarEmaArray = new double[60];
        //#define A_EMA  0.3333 	// 2/(N+1) N = 5 периодов
        double A_EMA = 0.5;

        public Form1()
        {
            InitializeComponent();



            xb = new XBee("COM7", 57600);
            xb.RF_Command_Responce_Received += new XBeeComandResponceReceivedEventHandler(xb_RF_Command_Responce_Received);
            xb.RF_Data_Received += new XBeeDataReceivedEventHandler(xb_RF_Data_Received);
            Console.WriteLine("XBee open port status = " + xb.OpenPort());
        }

        private void xb_RF_Command_Responce_Received(object sender, XBeeComandResponceReceivedEventArgs e)
        {
            Console.Write("Device addr = " + e.DeviceAddress.ToString() + " Network addr = " + e.NetAddress.ToString() + " Command " + e.Command + " status " + e.CommandStatus.ToString() + " Values: ");
            if (e.CommandResult != null)
            {
                for (int i = 0; i < e.CommandResult.Length; i++)
                {
                    Console.Write(e.CommandResult[i].ToString() + ";");
                }
                Console.WriteLine("");
            }
        }
        private void xb_RF_Data_Received(object sender, XBeeBinaryDataRecevedEventArgs e)
        {
            //TelemetryUpdater(e.DataString);
            //Invoke(Telemetry, buffer.Replace('.', ','));


            SonarVal = (UInt16)(e.DataArray[2] + e.DataArray[1] * 256);
            SonarEmaArray[ServoPos / 100] = (int)(A_EMA * SonarVal + (1 - A_EMA) * SonarEmaArray[ServoPos / 100]);

            ServoPos = (UInt16)(e.DataArray[3] + e.DataArray[4] * 256);
            ServoPosGrad = map(ServoPos, 0, 5600, 0, 180);
            ServoPosRad = ServoPosGrad * (Math.PI / 180);

            SonarArray[ServoPos / 100] = new Point((int)(Math.Cos(ServoPosRad) * SonarEmaArray[ServoPos / 100]) + center.X, -(int)(Math.Sin(ServoPosRad) * SonarEmaArray[ServoPos / 100]) + center.Y);
            Console.WriteLine("Address " + e.Address + " SonarVal:" + SonarVal.ToString() + " Servo pos:" + ServoPos.ToString());
            Invalidate();

        }

        private void Form1_Closing(object sender, FormClosingEventArgs e)
        {
            xb.ClosePort();
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawString("SonarVal:" + SonarVal.ToString(), new Font("Arial", 10), new SolidBrush(Color.Black), 10, 10);
            g.DrawString("ServoPos:" + ServoPosGrad.ToString(), new Font("Arial", 10), new SolidBrush(Color.Black), 10, 25);


            Point Servo_p = new Point((int)(Math.Cos(ServoPosRad) * SonarVal) + center.X, -(int)(Math.Sin(ServoPosRad) * SonarVal) + center.Y);
            g.DrawLine(DrawPen, center, Servo_p);
            //g.DrawLines(DrawPen, SonarArray);
            g.DrawEllipse(DrawPen, center.X - 100, center.Y - 100, 200, 200);
            g.DrawEllipse(DrawPen, center.X - 200, center.Y - 200, 400, 400);
            g.DrawEllipse(DrawPen, center.X - 300, center.Y - 300, 600, 600);
            g.DrawEllipse(DrawPen, center.X - 400, center.Y - 400, 800, 800);
            g.DrawEllipse(DrawPen, center.X - 500, center.Y - 500, 1000, 1000);
            g.DrawString("100", new Font("Arial", 10), new SolidBrush(Color.Black), center.X-10, center.Y-100);
            g.DrawString("200", new Font("Arial", 10), new SolidBrush(Color.Black), center.X - 10, center.Y - 200);
            g.DrawString("300", new Font("Arial", 10), new SolidBrush(Color.Black), center.X - 10, center.Y - 300);
            g.DrawString("400", new Font("Arial", 10), new SolidBrush(Color.Black), center.X - 10, center.Y - 400);
            g.DrawString("500", new Font("Arial", 10), new SolidBrush(Color.Black), center.X - 10, center.Y - 500);
            for (int i = 1;i<55;i++)
            {
                if (SonarArray[i] != center)
                    //g.DrawEllipse(DrawPen, SonarArray[i].X - 1, SonarArray[i].Y - 1, 2, 2);
                    g.DrawLine(DrawPen, SonarArray[i - 1], SonarArray[i]);
            }
             

        }


        private double map(long x, long in_min, long in_max, long out_min, long out_max)
        {
            return (x - in_min) * (out_max - out_min) / (in_max - in_min) + out_min;
        }

    }
}
