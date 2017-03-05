﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using GMap.NET.MapProviders;
using System.IO.Compression;
using System.Text;
using System.Globalization;

namespace kagv
{
    //mapinfo struct
    public struct MapInfo
    {
        public RectLatLng Area;
        public int Zoom;
        public GMapProvider Type;
        public bool MakeWorldFile;
        public bool MakeKmz;//WE DONT USE IT BUT STRUCT NEEDS IT

        public MapInfo(RectLatLng Area, int Zoom, GMapProvider Type, bool makeWorldFile, bool MakeKmz)
        {
            this.Area = Area;
            this.Zoom = Zoom;
            this.Type = Type;
            this.MakeWorldFile = makeWorldFile;
            this.MakeKmz = MakeKmz;//WE DONT USE IT BUT STRUCT NEEDS IT
        }
    }


    public partial class Screenshot : Form
    {
        gmaps gmap;
        RectLatLng AreaGpx = RectLatLng.Empty;

        BackgroundWorker bg = new BackgroundWorker();
        readonly List<GPoint> tileArea = new List<GPoint>();

        public Screenshot(gmaps main) //overloaded constructor
        {
            InitializeComponent();

            gmap = main;

            bg.WorkerReportsProgress = true;
            bg.WorkerSupportsCancellation = true;
            bg.DoWork += new DoWorkEventHandler(bg_DoWork);
            bg.ProgressChanged += new ProgressChangedEventHandler(bg_ProgressChanged);
            bg.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bg_RunWorkerCompleted);
        }

        void bg_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (!e.Cancelled)
            {
                if (e.Error != null)
                {
                    MessageBox.Show("Error:" + e.Error.ToString(), "GMap.NET", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (e.Result != null)
                {
                    try
                    {
                        Process.Start(e.Result as string);
                    }
                    catch
                    {
                    }
                }
            }

            pb_save.Value = 0;
            btn_save.Enabled = true;
            gmap.mymap.Refresh();
        }

        void bg_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            pb_save.Value = e.ProgressPercentage;

            GPoint p = (GPoint)e.UserState;
            this.Text = "Static Map maker: Downloading[" + p + "]: " + tileArea.IndexOf(p) + " of " + tileArea.Count;
        }

        void bg_DoWork(object sender, DoWorkEventArgs e)
        {
            MapInfo info = (MapInfo)e.Argument;
            if (!info.Area.IsEmpty)
            {
                string bigImage = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + Path.DirectorySeparatorChar + "GMap at zoom " + info.Zoom + " - " + info.Type + "-" + DateTime.Now.Ticks + ".jpg";
                e.Result = bigImage;

                // current area
                GPoint topLeftPx = info.Type.Projection.FromLatLngToPixel(info.Area.LocationTopLeft, info.Zoom);
                GPoint rightButtomPx = info.Type.Projection.FromLatLngToPixel(info.Area.Bottom, info.Area.Right, info.Zoom);
                GPoint pxDelta = new GPoint(rightButtomPx.X - topLeftPx.X, rightButtomPx.Y - topLeftPx.Y);
                GMap.NET.GSize maxOfTiles = info.Type.Projection.GetTileMatrixMaxXY(info.Zoom);

                int padding = info.MakeWorldFile || info.MakeKmz ? 0 : 22;
                {
                    using (Bitmap bmpDestination = new Bitmap((int)(pxDelta.X + padding * 2), (int)(pxDelta.Y + padding * 2)))
                    {
                        using (Graphics gfx = Graphics.FromImage(bmpDestination))
                        {
                            gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            gfx.SmoothingMode = SmoothingMode.HighQuality;

                            int i = 0;

                            // get tiles & combine into one
                            lock (tileArea)
                            {
                                foreach (var p in tileArea)
                                {
                                    if (bg.CancellationPending)
                                    {
                                        e.Cancel = true;
                                        return;
                                    }

                                    int pc = (int)(((double)++i / tileArea.Count) * 100);
                                    bg.ReportProgress(pc, p);

                                    foreach (var tp in info.Type.Overlays)
                                    {
                                        Exception ex;
                                        GMapImage tile;

                                        // tile number inversion(BottomLeft -> TopLeft) for pergo maps
                                        if (tp.InvertedAxisY)
                                        {
                                            tile = GMaps.Instance.GetImageFrom(tp, new GPoint(p.X, maxOfTiles.Height - p.Y), info.Zoom, out ex) as GMapImage;
                                        }
                                        else // ok
                                        {
                                            tile = GMaps.Instance.GetImageFrom(tp, p, info.Zoom, out ex) as GMapImage;
                                        }

                                        if (tile != null)
                                        {
                                            using (tile)
                                            {
                                                long x = p.X * info.Type.Projection.TileSize.Width - topLeftPx.X + padding;
                                                long y = p.Y * info.Type.Projection.TileSize.Width - topLeftPx.Y + padding;
                                                {
                                                    gfx.DrawImage(tile.Img, x, y, info.Type.Projection.TileSize.Width, info.Type.Projection.TileSize.Height);
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            // draw info
                            if (!info.MakeWorldFile)
                            {
                                System.Drawing.Rectangle rect = new System.Drawing.Rectangle();
                                {
                                    rect.Location = new System.Drawing.Point(padding, padding);
                                    rect.Size = new System.Drawing.Size((int)pxDelta.X, (int)pxDelta.Y);
                                }

                                using (Font f = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold))
                                {
                                    if (cb_drawinfo.Checked)
                                    {
                                        // draw bounds & coordinates
                                        using (Pen p = new Pen(Brushes.DimGray, 3))
                                        {
                                            p.DashStyle = System.Drawing.Drawing2D.DashStyle.DashDot;

                                            gfx.DrawRectangle(p, rect);

                                            string topleft = info.Area.LocationTopLeft.ToString();
                                            SizeF s = gfx.MeasureString(topleft, f);

                                            gfx.DrawString(topleft, f, p.Brush, rect.X + s.Height / 2, rect.Y + s.Height / 2);

                                            string rightBottom = new PointLatLng(info.Area.Bottom, info.Area.Right).ToString();
                                            SizeF s2 = gfx.MeasureString(rightBottom, f);

                                            gfx.DrawString(rightBottom, f, p.Brush, rect.Right - s2.Width - s2.Height / 2, rect.Bottom - s2.Height - s2.Height / 2);
                                        }
                                    }

                                    if (cb_drawscale.Checked)
                                    {
                                        // draw scale
                                        using (Pen p = new Pen(Brushes.Blue, 1))
                                        {
                                            double rez = info.Type.Projection.GetGroundResolution(info.Zoom, info.Area.Bottom);
                                            int px100 = (int)(100.0 / rez); // 100 meters
                                            int px1000 = (int)(1000.0 / rez); // 1km   

                                            gfx.DrawRectangle(p, rect.X + 10, rect.Bottom - 20, px1000, 10);
                                            gfx.DrawRectangle(p, rect.X + 10, rect.Bottom - 20, px100, 10);

                                            string leftBottom = "scale: 100m | 1Km";
                                            SizeF s = gfx.MeasureString(leftBottom, f);
                                            gfx.DrawString(leftBottom, f, p.Brush, rect.X + 10, rect.Bottom - s.Height - 20);
                                        }
                                    }
                                }
                            }
                        }

                        bmpDestination.Save(bigImage, ImageFormat.Jpeg);
                    }
                }

            }
        }


        private void btn_save_Click(object sender, EventArgs e)
        {
            RectLatLng? area = null; //abstract structure


            area = gmap.mymap.SelectedArea;
            if (area.Value.IsEmpty)
            {
                MessageBox.Show("Select map area holding ALT", "", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }


            if (!bg.IsBusy)
            {
                lock (tileArea)
                {
                    tileArea.Clear();
                    tileArea.AddRange(gmap.mymap.MapProvider.Projection.GetAreaTileList(area.Value, (int)gmap.mymap.Zoom, 1));
                    tileArea.TrimExcess();
                }


                pb_save.Value = 0;
                btn_save.Enabled = false;
                gmap.mymap.HoldInvalidation = true;

                bg.RunWorkerAsync(new MapInfo(area.Value, (int)gmap.mymap.Zoom, gmap.mymap.MapProvider, false, false));
                gmap.mymap.Refresh();
            }

        }

        private void btn_cancel_Click(object sender, EventArgs e)
        {
            if (bg.IsBusy)
            {
                bg.CancelAsync();
            }
            this.Close();
        }

        private void Screenshot_FormClosing(object sender, FormClosingEventArgs e)
        {
            tileArea.Clear();
        }

        private void Screenshot_Load(object sender, EventArgs e)
        {
            this.Location = gmap.Location;
        }
    }

    
}
