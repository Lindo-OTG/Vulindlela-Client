using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;
using Telerik.WinControls; 
using Telerik.WinControls.UI; 
using Telerik.WinControls.UI.Map; 
using Telerik.WinControls.UI.Map.Bing; 

namespace Client
{
    public partial class Vulindlela : Telerik.WinControls.UI.ShapedForm
    {
        private bool connected = false;
        private Thread client = null;
        private struct MyClient
        {
            public string username;
            public string key;
            public TcpClient client;
            public NetworkStream stream;
            public byte[] buffer;
            public StringBuilder data;
            public EventWaitHandle handle;
        };
        private MyClient obj;
        private Task send = null;
        private bool exit = false;
        private static List<string> logdetails = new List<string>();
        private static FileStream fs = new FileStream("ChatLog.txt", FileMode.Append, FileAccess.Write);
        private static StreamWriter writer = new StreamWriter(fs);

        public Vulindlela()
        {
            InitializeComponent();
            this.SetupProviders();
            this.SetupLayer();
        }

        #region Mapwork
        private void SetupProviders()
        {
            BingRestMapProvider bingProvider = new BingRestMapProvider();
            bingProvider.Culture = Thread.CurrentThread.CurrentCulture;
            bingProvider.UseSession = true;
            bingProvider.BingKey = "yytY9nWoLY1yzOVHVL28~B92gTMFASBaq8m21h5Ap6g~AqbQVX4w2C8D02JKLi4u3hQ9v7vkXa38ojuc9FzDWMYUgHnmBFBlkth4OW6ar4mW";
            bingProvider.InitializationComplete += delegate (object sender, EventArgs e)
            {
                this.map.MapElement.SearchBarElement.Search("Kempton Park, South Africa");               
                this.map.Zoom(8);
                this.map.MapElement.MiniMapElement.Zoom(12);
            };

            this.map.MapElement.Providers.Add(bingProvider);

            this.map.MapElement.SearchBarElement.SearchProvider = bingProvider;
            bingProvider.SearchCompleted += BingProvider_SearchCompleted;
            bingProvider.SearchError += BingProvider_SearchError;
        }

        private void BingProvider_SearchCompleted(object sender, SearchCompletedEventArgs e)
        {
            RectangleG allPoints = new RectangleG(double.MinValue, double.MaxValue, double.MaxValue, double.MinValue);
            this.map.Layers["Pins"].Clear();

            foreach (Location location in e.Locations)
            {
                PointG point = new PointG(location.Point.Coordinates[0], location.Point.Coordinates[1]);
                MapPin pin = new MapPin(point);
                pin.BackColor = Color.FromArgb(11, 195, 197);
                pin.ToolTipText = location.Address.FormattedAddress;
                this.map.MapElement.Layers["Pins"].Add(pin);

                MapCallout callout = new MapCallout(pin);
                callout.Text = location.Name;
                this.map.MapElement.Layers["Pins"].Add(callout);

                allPoints.North = Math.Max(allPoints.North, point.Latitude);
                allPoints.South = Math.Min(allPoints.South, point.Latitude);
                allPoints.West = Math.Min(allPoints.West, point.Longitude);
                allPoints.East = Math.Max(allPoints.East, point.Longitude);
            }

            if (e.Locations.Length > 0) 
            {
                if (e.Locations.Length == 1)
                {
                    this.map.BringIntoView(new PointG(e.Locations[0].Point.Coordinates[0], e.Locations[0].Point.Coordinates[1]));
                }
                else
                {
                    this.map.MapElement.BringIntoView(allPoints);
                    this.map.Zoom(this.map.MapElement.ZoomLevel - 1);
                }
            }
            else
            {
                RadMessageBox.Show("No result found for the provided search query!");
            }
        }

        private void BingProvider_SearchError(object sender, SearchErrorEventArgs e)
        {
            RadMessageBox.ThemeName = AppliationPages.ThemeName;
            RadMessageBox.Show(e.Error.Message);
        }

        private void SetupLayer()
        {
            MapLayer pinsLayer = new MapLayer("Pins");
            this.map.Layers.Add(pinsLayer);
        }

        #endregion

        #region Chat
        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (connected)
            {
                obj.client.Close();
            }
            else if (client == null || !client.IsAlive)
            {
                string address = addrTextBox.Text.Trim();
                string number = portTextBox.Text.Trim();
                string username = usernameTextBox.Text.Trim();
                bool error = false;
                IPAddress ip = null;
                if (address.Length < 1)
                {
                    error = true;
                    Log(SystemMsg("Address is required"));
                }
                else
                {
                    try
                    {
                        ip = Dns.Resolve(address).AddressList[0];
                    }
                    catch
                    {
                        error = true;
                        Log(SystemMsg("Address is not valid"));
                    }
                }
                int port = 0;
                if (number.Length < 1)
                {
                    error = true;
                    Log(SystemMsg("Port number is required"));
                }
                else if (!int.TryParse(number, out port))
                {
                    error = true;
                    Log(SystemMsg("Port number is not valid"));
                }
                else if (port < 0 || port > 65535)
                {
                    error = true;
                    Log(SystemMsg("Port number is out of range"));
                }
                if (username.Length < 1)
                {
                    error = true;
                    Log(SystemMsg("Username is required"));
                }
                if (!error)
                {
                    // encryption key is optional
                    client = new Thread(() => Connection(ip, port, username, keyTextBox.Text))
                    {
                        IsBackground = true
                    };
                    client.Start();
                }
            }
        }

        private void Log(string msg = "") // clear the log if message is not supplied or is empty
        {
            if (!exit)
            {
                logTextBox.Invoke((MethodInvoker)delegate
                {
                    if (msg.Length > 0)
                    {
                        logTextBox.AppendText(string.Format("[ {0} ] {1}{2}", DateTime.Now.ToString("HH:mm"), msg, Environment.NewLine));
                        logdetails.Add(string.Format("[ {0} ] {1}", DateTime.Now.ToString("HH:mm"), msg));
                    }
                    else
                    {
                        logTextBox.Clear();
                        logdetails.Clear();
                    }
                });
            }
        }

        private string ErrorMsg(string msg)
        {
            return string.Format("ERROR: {0}", msg);
        }

        private string SystemMsg(string msg)
        {
            return string.Format("SYSTEM: {0}", msg);
        }

        private void Connected(bool status)
        {
            if (!exit)
            {
                btnConnect.Invoke((MethodInvoker)delegate
                {
                    connected = status;
                    if (status)
                    {
                        addrTextBox.Enabled = false;
                        portTextBox.Enabled = false;
                        usernameTextBox.Enabled = false;
                        keyTextBox.Enabled = false;
                        btnConnect.Text = "Disconnect";
                        Log(SystemMsg("You are now connected"));
                    }
                    else
                    {
                        addrTextBox.Enabled = true;
                        portTextBox.Enabled = true;
                        usernameTextBox.Enabled = true;
                        keyTextBox.Enabled = true;
                        btnConnect.Text = "Connect";
                        Log(SystemMsg("You are now disconnected"));
                    }
                });
            }
        }

        private void Read(IAsyncResult result)
        {
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
            if (bytes > 0)
            {
                obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytes));
                try
                {
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                    }
                    else
                    {
                        Log(obj.data.ToString());
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private void ReadAuth(IAsyncResult result)
        {
            int bytes = 0;
            if (obj.client.Connected)
            {
                try
                {
                    bytes = obj.stream.EndRead(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
            if (bytes > 0)
            {
                obj.data.AppendFormat("{0}", Encoding.UTF8.GetString(obj.buffer, 0, bytes));
                try
                {
                    if (obj.stream.DataAvailable)
                    {
                        obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), null);
                    }
                    else
                    {
                        JavaScriptSerializer json = new JavaScriptSerializer();
                        Dictionary<string, string> data = json.Deserialize<Dictionary<string, string>>(obj.data.ToString());
                        if (data.ContainsKey("status") && data["status"].Equals("authorized"))
                        {
                            Connected(true);
                        }
                        obj.data.Clear();
                        obj.handle.Set();
                    }
                }
                catch (Exception ex)
                {
                    obj.data.Clear();
                    Log(ErrorMsg(ex.Message));
                    obj.handle.Set();
                }
            }
            else
            {
                obj.client.Close();
                obj.handle.Set();
            }
        }

        private bool Authorize()
        {
            bool success = false;
            Dictionary<string, string> data = new Dictionary<string, string>();
            data.Add("username", obj.username);
            data.Add("key", obj.key);
            JavaScriptSerializer json = new JavaScriptSerializer(); // feel free to use JSON serializer
            Send(json.Serialize(data));
            while (obj.client.Connected)
            {
                try
                {
                    obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(ReadAuth), null);
                    obj.handle.WaitOne();
                    if (connected)
                    {
                        success = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
            if (!connected)
            {
                Log(SystemMsg("Unauthorized"));
            }
            return success;
        }

        private void Connection(IPAddress ip, int port, string username, string key)
        {
            try
            {
                obj = new MyClient();
                obj.username = username;
                obj.key = key;
                obj.client = new TcpClient();
                obj.client.Connect(ip, port);
                obj.stream = obj.client.GetStream();
                obj.buffer = new byte[obj.client.ReceiveBufferSize];
                obj.data = new StringBuilder();
                obj.handle = new EventWaitHandle(false, EventResetMode.AutoReset);
                if (Authorize())
                {
                    while (obj.client.Connected)
                    {
                        try
                        {
                            obj.stream.BeginRead(obj.buffer, 0, obj.buffer.Length, new AsyncCallback(Read), null);
                            obj.handle.WaitOne();
                        }
                        catch (Exception ex)
                        {
                            Log(ErrorMsg(ex.Message));
                        }
                    }
                    obj.client.Close();
                    Connected(false);
                }
            }
            catch (Exception ex)
            {
                Log(ErrorMsg(ex.Message));
            }
        }

        private void Write(IAsyncResult result)
        {
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.EndWrite(result);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
        }

        private void BeginWrite(string msg)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            if (obj.client.Connected)
            {
                try
                {
                    obj.stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(Write), null);
                }
                catch (Exception ex)
                {
                    Log(ErrorMsg(ex.Message));
                }
            }
        }

        private void Send(string msg)
        {
            if (send == null || send.IsCompleted)
            {
                send = Task.Factory.StartNew(() => BeginWrite(msg));
            }
            else
            {
                send.ContinueWith(antecendent => BeginWrite(msg));
            }
        }

        private void SendTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (sendTextBox.Text.Length > 0)
                {
                    string msg = sendTextBox.Text;
                    sendTextBox.Clear();
                    Log(string.Format("You: {0}", msg));
                    if (connected)
                    {
                        Send(msg);
                    }
                }
            }
        }

        private void Vulindlela_FormClosing(object sender, FormClosingEventArgs e)
        {
            exit = true;
            if (connected)
            {
                obj.client.Close();
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            Log();
        }

        private void btnLog_Click(object sender, EventArgs e)
        {
            if (logdetails.Count != 0)
            {
                try
                {
                    writer.WriteLine(" \n\t\t\t\t\t" + DateTime.Today.Date.ToString().Substring(0, 9) + " Chats Log\n");

                    foreach (string item in logdetails)
                    {
                        writer.WriteLine(item);
                    }
                    writer.WriteLine("===========================================================================================================");
                    writer.Close();
                    fs.Close();
                    MessageBox.Show("Chat Details Successfully Saved To ChatLog file.", "Data Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception err)
                {
                    MessageBox.Show(err.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("Log List is empty, there is no data to Log", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void AddToGrid(string id, string name)
        {
            if (!exit)
            {
                clientsDataGridView.Invoke((MethodInvoker)delegate
                {
                    string[] row = new string[] { id, name };
                    clientsDataGridView.Rows.Add(row);
                    totalLabel.Text = string.Format("Total clients: {0}", clientsDataGridView.Rows.Count);
                });
            }
        }

        private void btnPrivateChat_Click(object sender, EventArgs e)
        {

        }
        #endregion

        #region Dashcam
        private void btnStartCam_Click(object sender, EventArgs e)
        {
            string filelocation = Environment.CurrentDirectory;
            txtFileLocation.Text = radWebCam1.RecordingFilePath = filelocation;
            radWebCam1.Visible = true;
            radWebCam1.Start();
            radWebCam1.StopRecording();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            radWebCam1.StopRecording();
            radWebCam1.Stop();
            radWebCam1.Visible = false;
        }
        private void btnRecord_Click(object sender, EventArgs e)
        {
            radWebCam1.StartRecording();
        }

        private void btnSnap_Click(object sender, EventArgs e)
        {
            radWebCam1.PreviewSnapshots = true;
            radWebCam1.TakeSnapshot();
        }

        private void btnSaveSnap_Click(object sender, EventArgs e)
        {
            radWebCam1.SaveSnapshot();
        }

        private void btnDiscard_Click(object sender, EventArgs e)
        {
            radWebCam1.DiscardSnapshot();
        }
        #endregion

        private void btnExit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
