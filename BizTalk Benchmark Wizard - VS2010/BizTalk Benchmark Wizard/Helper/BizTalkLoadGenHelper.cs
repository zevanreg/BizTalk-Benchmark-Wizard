﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using LoadGen;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Configuration;
using System.ServiceModel.Configuration;
using BizTalkBenchmarkWizard.PerformanceCounterHelper;


namespace BizTalk_Benchmark_Wizard.Helper
{
    internal class BizTalkLoadGenHelper
    {
        #region Delegats and events
        public delegate void CompleteHandler(object sender, LoadGenStopEventArgs e);
        public delegate void InitiateStepHandler(object sender, StepEventArgs e);
        public event CompleteHandler OnComplete;
        public event InitiateStepHandler OnStepComplete;

        #endregion
        #region Public members
        public double TestDuration = 120;
        public List<PerfCounter> PerfCounters = new List<PerfCounter>();
        #endregion
        #region Privare members
        PerformanceCounterLogger _performanceCounterLogger = null;
        LoadGen.LoadGen _loadGen = null;
        List<LoadGen.LoadGen> _loadGenClients = new List<LoadGen.LoadGen>();
        int _numberOfLoadGenStopped = 0;
        int _numberOfLoadGenClients = 0;
        List<LoadGenStopEventArgs> _allLoadGenStopEventArgs = new List<LoadGenStopEventArgs>();
        #endregion
        #region Public methods and constructor
        public BizTalkLoadGenHelper()
        {

        }
        public void InitPerfCounters_AppFabric(string scenario, string server)
        {
            string counterCategory = "BizTalk Benchmark Wizard";
            try
            {
                PerfCounter perfCounter = new PerfCounter();
                _performanceCounterLogger = new PerformanceCounterLogger(PerformanceCounterLogger.ServiceType.Both);
                
                perfCounter.ReceivedCounters.Add(_performanceCounterLogger.CallPerSecondTransmitCounter);
                perfCounter.CPUCounters1.Add(new PerformanceCounter("Processor", "% Processor Time", "_Total"));
                perfCounter.HasReceiveCounter = true;
                PerfCounters.Add(perfCounter);

                perfCounter.ProcessedCounters.Add(_performanceCounterLogger.CallPerSecondProcessedCounter);
                perfCounter.CPUCounters1.Add(new PerformanceCounter("Processor", "% Processor Time", "_Total", server));
                perfCounter.HasProcessingCounter = true;
                PerfCounters.Add(perfCounter);
                
                MainWindow.DoEvents();

                RaiseInitiateStepEvent("InitPerfCounters");
            }
            catch (Exception ex)
            {
                //InstallUtil /i /assemblyname "Microsoft.BizTalk.MsgBoxPerfCounters, Version=3.0.1.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35" 
                throw new ApplicationException(@"Unable to find PerfMon Counter. Make sure all BBW* host instances are started. If you lost the counters, this post might help you recover counters: ""http://blogs.msdn.com/biztalkperformance/archive/2007/09/30/how-to-manually-recreate-missing-biztalk-performance-counters.aspx""");
            }
        }
        public void InitPerfCounters_BizTalk(string scenario, List<HostMaping> hostmappings, Server msgBoxServer)
        {
            try
            {
                foreach (HostMaping hostMapping in hostmappings)
                {
                    PerfCounter perfCounter = new PerfCounter();
                    perfCounter.Server = hostMapping.SelectedHost;

                    switch (hostMapping.HostName)
                    {
                        case "BBW_RxHost":

                            UpdateServiceAddress(hostMapping.SelectedHost, scenario);

                            perfCounter.ReceivedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents received/Sec", "BBW_RxHost", hostMapping.SelectedHost));
                            perfCounter.CPUCounters1.Add(new PerformanceCounter("Processor", "% Processor Time", "_Total", hostMapping.SelectedHost));
                            perfCounter.HasReceiveCounter = true;
                            break;
                        case "BBW_PxHost":
                            break;
                        case "BBW_TxHost":
                            perfCounter.ProcessedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents processed/Sec", "BBW_TxHost", hostMapping.SelectedHost));
                            perfCounter.CPUCounters2.Add(new PerformanceCounter("Processor", "% Processor Time", "_Total", hostMapping.SelectedHost));
                            perfCounter.HasProcessingCounter = true;
                            break;
                    }
                    PerfCounters.Add(perfCounter);
                    MainWindow.DoEvents();
                }

                PerfCounter sqlPerfCounter = new PerfCounter();
                sqlPerfCounter.Server = msgBoxServer.Name;
                sqlPerfCounter.CPUCounters3.Add(new PerformanceCounter("Processor", "% Processor Time", "_Total", msgBoxServer.Name));
                PerfCounters.Add(sqlPerfCounter);

                string rcvHost = hostmappings.First(h => h.HostName == "BBW_RxHost").SelectedHost;

                RaiseInitiateStepEvent("InitPerfCounters");
                //_loadGenClients.Add(CreateAndStartLoadGenClient(CreateLoadGenScript(environment.LoadGenScriptFile, rcvHost), rcvHost));
            }
            catch (Exception)
            {
                //InstallUtil /i /assemblyname "Microsoft.BizTalk.MsgBoxPerfCounters, Version=3.0.1.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35" 
                throw new ApplicationException(@"Unable to find PerfMon Counter. Make sure all BBW* host instances are started. If you lost the counters, this post might help you recover counters: ""http://blogs.msdn.com/biztalkperformance/archive/2007/09/30/how-to-manually-recreate-missing-biztalk-performance-counters.aspx""");
            }
        }
        public void StartLoadGenClients_BizTalk(Environment environment, List<HostMaping> hostmappings, int testDuration)
        {
            string rcvHost = hostmappings.First(h => h.HostName == "BBW_RxHost").SelectedHost;

            CreateAndStartLoadGenClient(CreateLoadGenScript(environment.LoadGenScriptFile, rcvHost, testDuration), rcvHost);
            RaiseInitiateStepEvent("StartLoadGenClients");
            
        }
        public void StartLoadGenClients_AppFabric(Environment environment,string receiveHost, int testDuration)
        {
            CreateAndStartLoadGenClient(CreateLoadGenScript(environment.LoadGenScriptFile, receiveHost, testDuration), receiveHost);
            RaiseInitiateStepEvent("StartLoadGenClients");
            
        }
        public void StopAllTests()
        {
            foreach (LoadGen.LoadGen loadGen in _loadGenClients)
                loadGen.Stop();
        }
        public bool TestIndigoService(string server)
        {
            try
            {
                IndigoService.ServiceTwoWaysVoidNonTransactionalClient proxy = new BizTalk_Benchmark_Wizard.IndigoService.ServiceTwoWaysVoidNonTransactionalClient("IndigoService");

                System.ServiceModel.EndpointAddress newAddress =
                    new System.ServiceModel.EndpointAddress(string.Format("{0}://{1}:{2}{3}",
                        proxy.Endpoint.Address.Uri.Scheme,
                        server,
                        proxy.Endpoint.Address.Uri.Port.ToString(),
                        proxy.Endpoint.Address.Uri.AbsolutePath));

                proxy.Endpoint.Address = newAddress;

                string xml = "<Response><resp>This is a response</resp></Response>";

                using (proxy as IDisposable)
                {
                    System.ServiceModel.Channels.MessageVersion version = System.ServiceModel.Channels.MessageVersion.Soap12WSAddressing10;
                    MemoryStream stream = new MemoryStream(Encoding.Default.GetBytes(xml), 0, Encoding.Default.GetBytes(xml).Length);
                    stream.Seek(0L, SeekOrigin.Begin);
                    XmlTextReader reader = new XmlTextReader(stream);
                    System.ServiceModel.Channels.Message request = System.ServiceModel.Channels.Message.CreateMessage(version, "http://tempuri.org/IServiceTwoWaysVoidNonTransactional/ConsumeMessage", (XmlReader)reader);


                   proxy.ConsumeMessage(request);
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
        #endregion
        #region Private methods
        private string CreateLoadGenScript(string template, string server, int testDuration)
        {
            string rootPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Resources\\LoadGenScripts");

            string newScriptFile = Path.Combine(rootPath, server + "_LoadGenScript.xml");
            if (File.Exists(newScriptFile))
                File.Delete(newScriptFile);

            StreamWriter writer = new StreamWriter(newScriptFile);

            using (StreamReader reader = new StreamReader(Path.Combine(rootPath, template)))
            {
                while (reader.Peek() >= 0)
                {
                    string newLine = reader.ReadLine();
                    if (newLine.Trim().StartsWith("<TotalTime>"))
                        newLine = string.Format("<TotalTime>{0}</TotalTime>", testDuration);
                    newLine = newLine.Replace("@ServerName", server);
                    newLine = newLine.Replace("@FilePath", rootPath);
                    writer.WriteLine(newLine);
                }
            }
            writer.Close();
            return newScriptFile;
        }
        private LoadGen.LoadGen CreateAndStartLoadGenClient(string scriptFile, string server)
        {

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(scriptFile);
                TestDuration = long.Parse(doc.SelectSingleNode("LoadGenFramework/CommonSection/StopMode/TotalTime").InnerText);

                if (string.Compare(doc.FirstChild.Name, "LoadGenFramework", true, new CultureInfo("en-US")) != 0)
                {
                    throw new ConfigException("LoadGen Configuration File Schema Invalid!");
                }

                _numberOfLoadGenClients++;
                _loadGen = new LoadGen.LoadGen(doc.FirstChild);
                _loadGen.LoadGenStopped += new LoadGenEventHandler(LoadGen_Stopped);
                _loadGen.Start();
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception)
            {
                throw;
            }

            return _loadGen;
        }
        //private void CreatePerfCounter(string server)
        //{
        //    PerfCounter perfCounter = new PerfCounter();
        //    perfCounter.Server = server;

        //    perfCounter.ProcessedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents processed/Sec", "BBW_TxHost", server));

        //    perfCounter.ReceivedCounters.Add(new PerformanceCounter("BizTalk:Messaging", "Documents received/Sec", "BBW_RxHost", server));

        //    perfCounter.CPUCounters.Add(new PerformanceCounter("Processor", "% Processor Time", "_Total", server));

        //    PerfCounters.Add(perfCounter);
        //}
        protected void RaiseCompleteEvent(object sender, LoadGenStopEventArgs e)
        {
            if (OnComplete != null)
            {
                OnComplete(sender, e);
            }
        }
        void RaiseInitiateStepEvent(string eventStep)
        {
            if (OnStepComplete != null)
            {
                OnStepComplete(null, new StepEventArgs() { EventStep = eventStep });
            }
        }
        private void LoadGen_Stopped(object sender, LoadGenStopEventArgs e)
        {
            _allLoadGenStopEventArgs.Add(e);
            _numberOfLoadGenStopped++;

            if (_numberOfLoadGenClients == _numberOfLoadGenStopped)
            {
                try
                {
                    long numberOfMsgsSent = _allLoadGenStopEventArgs.Sum(l => l.NumFilesSent);
                    DateTime startTime = _allLoadGenStopEventArgs.Min(l => l.LoadGenStartTime);
                    DateTime stopTime = e.LoadGenStopTime;

                    LoadGenStopEventArgs ea = new LoadGenStopEventArgs(numberOfMsgsSent, startTime, stopTime);
                    RaiseCompleteEvent(sender, ea);
                }
                catch (Exception)
                {
                    RaiseCompleteEvent(this, new LoadGenStopEventArgs(1, DateTime.Now, DateTime.Now));
                }
            }
        }
        private void UpdateServiceAddress(string address, string endpointName)
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(System.Reflection.Assembly.GetExecutingAssembly().Location);

            ClientSection clientSection = (ClientSection)config.GetSection("system.serviceModel/client");
            ChannelEndpointElement endpointElement = null;
            foreach (ChannelEndpointElement element in clientSection.Endpoints)
            {
                if (element.Name == endpointName)
                {
                    endpointElement = element;
                    break;
                }
            }
            if (endpointElement != null)
            {
                endpointElement.Address = new Uri(string.Format("{0}://{1}:{2}{3}",
                            endpointElement.Address.Scheme,
                            address,
                            endpointElement.Address.Port.ToString(),
                            endpointElement.Address.AbsolutePath));


                config.Save();
                ConfigurationManager.RefreshSection("system.serviceModel/client");


            }
            else
            {
                throw new ApplicationException(string.Format("Could not find {0} endpoint configuration section", endpointName));
            }
        }
        #endregion
    }
    /// <summary>
    /// Used for collecting counter data
    /// </summary>
    public class PerfCounter
    {
        public bool HasProcessingCounter = false;
        public bool HasReceiveCounter = false;

        public List<PerformanceCounter> ProcessedCounters = new List<PerformanceCounter>();
        public List<PerformanceCounter> ReceivedCounters = new List<PerformanceCounter>();
        public List<PerformanceCounter> CPUCounters1 = new List<PerformanceCounter>();
        public List<PerformanceCounter> CPUCounters2 = new List<PerformanceCounter>();
        public List<PerformanceCounter> CPUCounters3 = new List<PerformanceCounter>();

        public float ProcessedCounterValue
        {
            get
            {
                float ret = 1;
                try
                {
                    foreach (PerformanceCounter c in this.ProcessedCounters)
                        ret += c.NextValue();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("Unable to collect perfcounter. Make sure you have all BBW* host instances are started.", ex);
                }
                return ret;
            }
        }
        public float ReceivedCounterValue
        {
            get
            {
                float ret = 1;
                try
                {
                    foreach (PerformanceCounter c in this.ReceivedCounters)
                        ret += c.NextValue();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("Unable to collect perfcounter. Make sure you run the application with elevated rights", ex);
                }
                return ret;
            }
        }
        public float CPUCounterValue1
        {
            get
            {
                float ret = 1;
                try
                {
                    foreach (PerformanceCounter c in this.CPUCounters1)
                        ret += c.NextValue();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("Unable to collect perfcounter. Make sure you run the application with elevated rights", ex);
                }
                return ret;
            }
        }
        public float CPUCounterValue2
        {
            get
            {
                float ret = 1;
                try
                {
                    foreach (PerformanceCounter c in this.CPUCounters2)
                        ret += c.NextValue();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("Unable to collect perfcounter. Make sure you run the application with elevated rights", ex);
                }
                return ret;
            }
        }
        public float CPUCounterValue3
        {
            get
            {
                float ret = 1;
                try
                {
                    foreach (PerformanceCounter c in this.CPUCounters3)
                        ret += c.NextValue();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException("Unable to collect perfcounter. Make sure you run the application with elevated rights", ex);
                }
                return ret;
            }
        }

        public string Server = string.Empty;
    }
    [System.ServiceModel.ServiceContract]
    public interface IServiceTwoWaysVoidNonTransactional
    {
        // Methods
        [System.ServiceModel.OperationContract(Action = "*")]
        void ConsumeMessage(System.ServiceModel.Channels.Message msg);
    }
    public class StepEventArgs : EventArgs
    {
        public string EventStep { get; set; }
    }
}
