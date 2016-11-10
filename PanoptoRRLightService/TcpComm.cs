using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Diagnostics;
using tcpServer;

namespace RRLightProgram
{
    public class TcpComm : IConsole
    {
        public TcpServer server = new TcpServer();

        private IStateMachine stateMachine;

        /// <summary>
        /// Controller of the remote recorder.
        /// This is used to get recording info from the remote controller.
        /// </summary>
        private RemoteRecorderSync remoteRecorder;

        /// <summary>
        ///     Constructor. Open the server on port given in settings.
        /// </summary>
        /// <param name="stateMachine">interface to the state machine</param>
        public TcpComm(IStateMachine stateMachine, RemoteRecorderSync remoteRecorder)
        {
            if (remoteRecorder == null)
            {
                throw new ArgumentException("remoteRecorder cannot be null.");
            }

            this.remoteRecorder = remoteRecorder;

            // check the settings to make sure we've be asked to run the tcp server and validate the port number. If not, return.
            if ((!Properties.Settings.Default.TcpServer) && (Properties.Settings.Default.TcpServerPort > 0))
                return;

            try
            {
                server.Port = RRLightProgram.Properties.Settings.Default.TcpServerPort;
                server.IdleTime = 50;
                server.IsOpen = false;
                server.MaxCallbackThreads = 100;
                server.MaxSendAttempts = 3;
                server.VerifyConnectionInterval = 0;
                server.OnDataAvailable += new tcpServerConnectionChanged(server_OnDataAvailable);
                server.OnConnect += new tcpServerConnectionChanged(server_OnConnect);

                server.Open();

                this.stateMachine = stateMachine;

                Trace.TraceInformation(DateTime.Now + ": TCP - Starting TCP Server on port {0}", RRLightProgram.Properties.Settings.Default.TcpServerPort);
                Trace.Flush();

            }
            catch (Exception e)
            {
                Trace.TraceInformation(DateTime.Now + ": TCP - Error starting TCP Server: {0}", e.Message);
                Trace.Flush();

                server = null;
            }

        }

        /// <summary>
        ///     Callback when a client connects
        /// </summary>
        /// <param name="connection">The TCP connection handler</param>
        /// 
        private void server_OnConnect(tcpServer.TcpServerConnection connection)
        {
            // Tell us who has connected

            Trace.TraceInformation(DateTime.Now + ": TCP - New client connection from {0}", connection.Socket.Client.RemoteEndPoint);
            Trace.Flush();
            
        }

        /// <summary>
        ///     Callback when a when data is available from the connection. 
        ///     Parses response and submits an input event to the state machine.
        /// </summary>
        /// <param name="connection">The TCP connection handler</param>
        /// 
        private void server_OnDataAvailable(tcpServer.TcpServerConnection connection)
        {
            byte[] data = readStream(connection.Socket);

            if (data != null)
            {
                // Remove line endings
                string inputString = Encoding.ASCII.GetString(data).TrimEnd('\n','\r');

                Command inputCommand;

                Trace.TraceInformation(DateTime.Now + ": TCP - Rx: " + inputString);
                Trace.Flush();

                //Fire the command event.
                if (Enum.TryParse(inputString, true, out inputCommand))
                {
                    Input inputSMInput;

                    // State machine input?
                    if (Enum.TryParse("Command" + inputString, true, out inputSMInput))
                    {
                        this.stateMachine.PostInput(inputSMInput);
                    }
                    else if (inputCommand == Command.Status)
                    {
                        this.OutputStatus(inputString);
                    }
                    else
                    {
                        Trace.TraceError(DateTime.Now + ": TCP - Unhandled command '{0}'", inputString);
                        this.Output("Error: Unhandled console command: " + inputString);
                    }
                }
                else
                {
                    Trace.TraceInformation(DateTime.Now + ": TCP - Command '{0}' not found", inputString);
                    Trace.Flush();

                    Output("TCP-Error: Command not found: " + inputString);
                }


            }

        }

        private void OutputStatus(string inputCommand)
        {
            var currentRecording = this.remoteRecorder.GetCurrentRecording();
            var nextRecording = this.remoteRecorder.GetNextRecording();

            this.Output("Recorder-Status: " + this.stateMachine.GetCurrentState());

            if (currentRecording != null)
            {
                this.Output("CurrentRecording-Id: " + currentRecording.Id);
                this.Output("CurrentRecording-Name: " + currentRecording.Name);
                this.Output("CurrentRecording-StartTime: " + currentRecording.StartTime.ToLocalTime());
                this.Output("CurrentRecording-EndTime: " + currentRecording.EndTime.ToLocalTime());
                this.Output("CurrentRecording-MinutesUntilStartTime: " +
                    (int)(currentRecording.StartTime.ToLocalTime() - DateTime.Now.ToLocalTime()).TotalMinutes);
                this.Output("CurrentRecording-MinutesUntilEndTime: " +
                    (int)(currentRecording.EndTime.ToLocalTime() - DateTime.Now.ToLocalTime()).TotalMinutes);
            }
            if (nextRecording != null)
            {
                this.Output("NextRecording-Id: " + nextRecording.Id);
                this.Output("NextRecording-Name: " + nextRecording.Name);
                this.Output("NextRecording-StartTime: " + nextRecording.StartTime.ToLocalTime());
                this.Output("NextRecording-EndTime: " + nextRecording.EndTime.ToLocalTime());
                this.Output("NextRecording-MinutesUntilStartTime: " +
                    (int)(nextRecording.StartTime.ToLocalTime() - DateTime.Now.ToLocalTime()).TotalMinutes);
                this.Output("NextRecording-MinutesUntilEndTime: " +
                    (int)(nextRecording.EndTime.ToLocalTime() - DateTime.Now.ToLocalTime()).TotalMinutes);
            }
        }

        /// <summary>
        ///     Read the data stream from the connection. 
        /// </summary>
        /// <param name="client">The TCP connection handler</param>
        /// <returns>data if available</returns>
        /// 
        protected byte[] readStream(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            if (stream.DataAvailable)
            {
                byte[] data = new byte[client.Available];

                int bytesRead = 0;
                try
                {
                    bytesRead = stream.Read(data, 0, data.Length);
                }
                catch (IOException)
                {
                }

                if (bytesRead < data.Length)
                {
                    byte[] lastData = data;
                    data = new byte[bytesRead];
                    Array.ConstrainedCopy(lastData, 0, data, 0, bytesRead);
                }
                return data;
            }
            return null;
        }

        /// <summary>
        ///     Public interface to send data to the client. 
        /// </summary>
        /// <param name="str">The data to send</param>
        /// 
        public void Output(String str)
        {
            if (server == null || !server.IsOpen)
                return;

            Trace.TraceInformation(DateTime.Now + ": TCP - Tx: " + str);
            Trace.Flush();

            server.Send(str + "\n");


        }


        /// <summary>
        ///     Close the server on a request to stop. 
        /// </summary>
        /// 
        public void Stop()
        {
            server.Close();
        }


    }
}
