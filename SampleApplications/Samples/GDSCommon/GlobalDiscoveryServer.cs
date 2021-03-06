﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Opc.Ua.Client;
using Opc.Ua.Gds;
using Opc.Ua.Configuration;
using Opc.Ua.Client.Controls;

namespace Opc.Ua.Gds
{
    /// <summary>
    /// A class that provides access to a Global Discovery Server.
    /// </summary>
    public class GlobalDiscoveryServer
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="GlobalDiscoveryServer"/> class.
        /// </summary>
        /// <param name="application">The application.</param>
        public GlobalDiscoveryServer(ApplicationInstance application)
        {
            m_application = application;
            m_endpointUrl = "opc.tcp://localhost:58810/GlobalDiscoveryServer";
            m_adminCredentials = new UserIdentity("appadmin", "demo");
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the application.
        /// </summary>
        /// <value>
        /// The application.
        /// </value>
        public ApplicationInstance Application
        {
            get { return m_application; }
        }

        /// <summary>
        /// Gets or sets the admin credentials.
        /// </summary>
        /// <value>
        /// The admin credentials.
        /// </value>
        public UserIdentity AdminCredentials
        {
            get { return m_adminCredentials; }
            set { m_adminCredentials = value; }
        }

        /// <summary>
        /// Raised when admin credentials are required.
        /// </summary>
        public event AdminCredentialsRequiredEventHandler AdminCredentialsRequired;

        /// <summary>
        /// Gets or sets the endpoint URL.
        /// </summary>
        /// <value>
        /// The endpoint URL.
        /// </value>
        public string EndpointUrl
        {
            get { return m_endpointUrl; }
            set { m_endpointUrl = value; }
        }

        /// <summary>
        /// Gets or sets the preferred locales.
        /// </summary>
        /// <value>
        /// The preferred locales.
        /// </value>
        public string[] PreferredLocales
        {
            get { return m_preferredLocales; }
            set { m_preferredLocales = value; }
        }

        /// <summary>
        /// Gets a value indicating whether a session is connected.
        /// </summary>
        /// <value>
        ///   <c>true</c> if [is connected]; otherwise, <c>false</c>.
        /// </value>
        public bool IsConnected { get { return m_session != null && m_session.Connected; } }
        #endregion

        #region Public Methods
        /// <summary>
        /// Selects the default GDS.
        /// </summary>
        /// <param name="lds">The LDS to use.</param>
        /// <returns>
        /// TRUE if successful; FALSE otherwise.
        /// </returns>
        public bool SelectDefaultGds(LocalDiscoveryServer lds)
        {
            List<string> gdsUrls = new List<string>();

            try
            {
                DateTime lastResetTime;

                if (lds == null)
                {
                    lds = new LocalDiscoveryServer(this.Application.ApplicationConfiguration);
                }

                // gdsUrls.Add("opc.tcp://bronze-b:58810/GlobalDiscoveryServer");

                var servers = lds.FindServersOnNetwork(0, 1000, out lastResetTime);

                foreach (var server in servers)
                {
                    if (server.ServerCapabilities != null && server.ServerCapabilities.Contains(ServerCapability.GlobalDiscoveryServer))
                    {
                        gdsUrls.Add(server.DiscoveryUrl);
                    }
                }
            }
            catch (Exception exception)
            {
                Utils.Trace(exception, "Unexpected error connecting to LDS");
            }

            string url = new SelectGdsDialog().ShowDialog(null, this, gdsUrls);

            if (url != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Connects the specified endpoint URL.
        /// </summary>
        /// <param name="endpointUrl">The endpoint URL.</param>
        /// <exception cref="System.ArgumentNullException">endpointUrl</exception>
        /// <exception cref="System.ArgumentException">endpointUrl</exception>
        public async void Connect(string endpointUrl)
        {
            if (endpointUrl == null)
            {
                endpointUrl = m_endpointUrl;
            }

            if (String.IsNullOrEmpty(endpointUrl))
            {
                throw new ArgumentNullException("endpointUrl");
            }

            if (!Uri.IsWellFormedUriString(endpointUrl, UriKind.Absolute))
            {
                throw new ArgumentException(endpointUrl + " is not a valid URL.", "endpointUrl");
            }

            if (m_session != null)
            {
                m_session.Dispose();
                m_session = null;
            }

            EndpointDescription endpointDescription = ClientUtils.SelectEndpoint(endpointUrl, true);
            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(m_application.ApplicationConfiguration);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            m_session = await Session.Create(
                m_application.ApplicationConfiguration,
                endpoint,
                true,
                false,
                m_application.ApplicationName,
                60000,
                AdminCredentials,
                m_preferredLocales);

            m_session.SessionClosing += Session_SessionClosing;
            m_session.KeepAlive += Session_KeepAlive;

            if (m_session.Factory.GetSystemType(Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType) == null)
            {
                m_session.Factory.AddEncodeableTypes(typeof(Opc.Ua.Gds.ObjectIds).Assembly);
            }

            m_session.ReturnDiagnostics = DiagnosticsMasks.SymbolicIdAndText;
            m_endpointUrl = m_session.ConfiguredEndpoint.EndpointUrl.ToString();
        }

        private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            if (ServiceResult.IsBad(e.Status))
            {
                m_session.Dispose();
                m_session = null;
            }
        }

        private void Session_SessionClosing(object sender, EventArgs e)
        {
            m_session.Dispose();
            m_session = null;
        }
        #endregion

        #region GDS Methods
        /// <summary>
        /// Finds the applications with the specified application uri.
        /// </summary>
        /// <param name="applicationUri">The application URI.</param>
        /// <returns>The matching application.</returns>
        public ApplicationRecordDataType[] FindApplication(string applicationUri)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_FindApplications, m_session.NamespaceUris),
                applicationUri);

            ApplicationRecordDataType[] applications = null;

            if (outputArguments.Count > 0)
            {
                applications = (ApplicationRecordDataType[])ExtensionObject.ToArray(outputArguments[0] as ExtensionObject[], typeof(ApplicationRecordDataType));
            }

            return applications;
        }

        /// <summary>
        /// Queries the GDS for any servers matching the criteria.
        /// </summary>
        /// <param name="maxRecordsToReturn">The max records to return.</param>
        /// <param name="applicationName">The filter applied to the application name.</param>
        /// <param name="applicationUri">The filter applied to the application uri.</param>
        /// <param name="productUri">The filter applied to the product uri.</param>
        /// <param name="serverCapabilities">The filter applied to the server capabilities.</param>
        /// <returns>A enumarator used to access the results.</returns>
        public IEnumerable<ServerOnNetwork> QueryServers(
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            IList<string> serverCapabilities)
        {
            return new ServerOnNetworkCollection(new ServerEnumerator(
                this, 
                maxRecordsToReturn, 
                applicationName, 
                applicationUri,
                productUri, 
                serverCapabilities));
        }

        #region Query Server Helpers
        internal ServerOnNetwork[] QueryServers(
            ref DateTime lastResetTime,
            ref uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            string productUri,
            IList<string> serverCapabilities)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_QueryServers, m_session.NamespaceUris),
                startingRecordId,
                maxRecordsToReturn,
                applicationName,
                applicationUri,
                productUri,
                serverCapabilities);

            ServerOnNetwork[] servers = null;

            if (outputArguments.Count > 0)
            {
                if (lastResetTime != DateTime.MinValue && lastResetTime < (DateTime)outputArguments[0])
                {
                    throw new InvalidOperationException("Enumeration cannot continue because the Server has reset its index.");
                }

                lastResetTime = (DateTime)outputArguments[0];
            }

            if (outputArguments.Count > 1)
            {
                servers = (ServerOnNetwork[])ExtensionObject.ToArray(outputArguments[1] as ExtensionObject[], typeof(ServerOnNetwork));
            }

            if (servers != null && servers.Length > 0)
            {
                startingRecordId = servers[servers.Length - 1].RecordId + 1;
            }

            return servers;
        }

        #region ServerEnumerator Class
        internal class ServerEnumerator : IEnumerator<ServerOnNetwork>
        {
            internal ServerEnumerator(
                GlobalDiscoveryServer gds,
                uint maxRecordsToReturn,
                string applicationName,
                string applicationUri,
                string productUri,
                IList<string> serverCapabilities)
            {
                m_gds = gds;
                m_maxRecordsToReturn = maxRecordsToReturn;
                m_applicationName = applicationName;
                m_applicationUri = applicationUri;
                m_productUri = productUri;
                m_serverCapabilities = serverCapabilities;
                m_lastResetTime = DateTime.MinValue;
            }

            #region IEnumerator<ServerOnNetwork> Members
            public ServerOnNetwork Current
            {
                get
                {
                    if (m_servers != null && m_index >= 0 && m_index < m_servers.Count)
                    {
                        return m_servers[m_index];
                    }

                    return null;
                }
            }

            public void Dispose()
            {
                // nothing to do.
            }

            object System.Collections.IEnumerator.Current
            {
                get 
                {
                    return this.Current;
                }
            }

            public bool MoveNext()
            {
                m_index++;

                if (m_servers != null && m_index >= 0 && m_index < m_servers.Count)
                {
                    return true;
                }

                var servers = m_gds.QueryServers(
                    ref m_lastResetTime,
                    ref m_startingRecordId,
                    m_maxRecordsToReturn,
                    m_applicationName,
                    m_applicationUri,
                    m_productUri,
                    m_serverCapabilities);

                if (servers != null)
                {
                    if (m_servers == null)
                    {
                        m_index = 0;
                        m_servers = new List<ServerOnNetwork>();
                    }

                    m_servers.AddRange(servers);

                    if (m_index < m_servers.Count)
                    {
                        return true;
                    }
                }

                return false;
            }

            public void Reset()
            {
                m_index = 0;
            }
            #endregion

            #region Private Fields
            private GlobalDiscoveryServer m_gds;
            private uint m_maxRecordsToReturn;
            private string m_applicationName;
            private string m_applicationUri;
            private string m_productUri;
            private IList<string> m_serverCapabilities;
            private uint m_startingRecordId;
            private DateTime m_lastResetTime;
            private List<ServerOnNetwork> m_servers;
            private int m_index;
            #endregion
        }
        #endregion

        #region ServerOnNetworkCollection Class
        internal class ServerOnNetworkCollection : IEnumerable<ServerOnNetwork>
        {
            public ServerOnNetworkCollection(ServerEnumerator enumerator)
            {
                m_enumerator = enumerator;
            }

            public IEnumerator<ServerOnNetwork> GetEnumerator()
            {
                return m_enumerator;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return m_enumerator;
            }

            private ServerEnumerator m_enumerator;
        }
        #endregion
        #endregion

        /// <summary>
        /// Get the application record.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <returns>The application record for the specified application id.</returns>
        public ApplicationRecordDataType GetApplication(NodeId applicationId)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_GetApplication, m_session.NamespaceUris),
                applicationId);

            if (outputArguments.Count > 0)
            {
                return ExtensionObject.ToEncodeable(outputArguments[0] as ExtensionObject) as ApplicationRecordDataType;
            }

            return null;
        }

        /// <summary>
        /// Registers the application.
        /// </summary>
        /// <param name="application">The application.</param>
        /// <returns>The application id assigned to the application.</returns>
        public NodeId RegisterApplication(ApplicationRecordDataType application)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_RegisterApplication, m_session.NamespaceUris),
                application);

            if (outputArguments.Count > 0)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Unregisters the application.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        public void UnregisterApplication(NodeId applicationId)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_UnregisterApplication, m_session.NamespaceUris),
                applicationId);
        }

        /// <summary>
        /// Requests a new certificate.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">The authority.</param>
        /// <param name="certificateTypeId">Type of the certificate.</param>
        /// <param name="subjectName">Name of the subject.</param>
        /// <param name="domainNames">The domain names.</param>
        /// <param name="privateKeyFormat">The private key format (PEM or PFX).</param>
        /// <param name="privateKeyPassword">The private key password.</param>
        /// <returns>
        /// The id for the request which is used to check when it is approved.
        /// </returns>
        public NodeId StartNewKeyPairRequest(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            IList<string> domainNames,
            string privateKeyFormat,
            string privateKeyPassword)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_StartNewKeyPairRequest, m_session.NamespaceUris),
                applicationId,
                certificateGroupId,
                certificateTypeId,
                subjectName,
                domainNames,
                privateKeyFormat,
                privateKeyPassword);

            if (outputArguments.Count > 0)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Signs the certificate.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificate">The certificate to renew.</param>
        /// <returns>The id for the request which is used to check when it is approved.</returns>
        public NodeId StartSigningRequest(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            byte[] certificateRequest)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_StartSigningRequest, m_session.NamespaceUris),
                applicationId,
                certificateGroupId,
                certificateTypeId,
                certificateRequest);

            if (outputArguments.Count > 0)
            {
                return outputArguments[0] as NodeId;
            }

            return null;
        }
        /// <summary>
        /// Checks the request status.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="requestId">The request id.</param>
        /// <param name="privateKey">The private key.</param>
        /// <param name="issuerCertificates">The issuer certificates.</param>
        /// <returns>The public key.</returns>
        public byte[] FinishRequest(
            NodeId applicationId,
            NodeId requestId,
            out byte[] privateKey,
            out byte[][] issuerCertificates)
        {
            privateKey = null;
            issuerCertificates = null;

            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_FinishRequest, m_session.NamespaceUris),
                applicationId,
                requestId);

            byte[] certificate = null;

            if (outputArguments.Count > 0)
            {
                certificate = outputArguments[0] as byte[];
            }

            if (outputArguments.Count > 1)
            {
                privateKey = outputArguments[1] as byte[];
            }

            if (outputArguments.Count > 2)
            {
                issuerCertificates = outputArguments[2] as byte[][];
            }

            return certificate;
        }

        /// <summary>
        /// Gets the certificate groups.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <returns></returns>
        public NodeId[] GetCertificateGroups(
            NodeId applicationId)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_GetTrustList, m_session.NamespaceUris),
                applicationId);

            if (outputArguments.Count > 0)
            {
                return outputArguments[0] as NodeId[];
            }

            return null;
        }

        /// <summary>
        /// Gets the trust lists method.
        /// </summary>
        /// <param name="applicationId">The application id.</param>
        /// <param name="certificateGroupId">Type of the trust list.</param>
        /// <returns></returns>
        public NodeId GetTrustList(
            NodeId applicationId,
            NodeId certificateGroupId)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.ObjectIds.Directory, m_session.NamespaceUris),
                ExpandedNodeId.ToNodeId(Opc.Ua.Gds.MethodIds.Directory_GetTrustList, m_session.NamespaceUris),
                applicationId,
                certificateGroupId);

            if (outputArguments.Count > 0)
            {
                 return outputArguments[0] as NodeId;
            }

            return null;
        }

        /// <summary>
        /// Reads the trust list.
        /// </summary>
        public TrustListDataType ReadTrustList(NodeId trustListId)
        {
            if (!IsConnected)
            {
                Connect(null);
            }

            var outputArguments = m_session.Call(
                trustListId,
                Opc.Ua.MethodIds.FileType_Open,
                (byte)1);

            uint fileHandle = (uint)outputArguments[0];
            MemoryStream ostrm = new MemoryStream();

            try
            {
                while (true)
                {
                    int length = 4096;

                    outputArguments = m_session.Call(
                        trustListId,
                        Opc.Ua.MethodIds.FileType_Read,
                        fileHandle,
                        length);

                    byte[] bytes = (byte[])outputArguments[0];
                    ostrm.Write(bytes, 0, bytes.Length);

                    if (length != bytes.Length)
                    {
                        break;
                    }
                }
            }
            catch (Exception)
            {

                throw;
            }
            finally
            {
                if (IsConnected)
                {
                    m_session.Call(
                        trustListId,
                        Opc.Ua.MethodIds.FileType_Close,
                        fileHandle);
                }
            }

            ostrm.Position = 0;

            BinaryDecoder decoder = new BinaryDecoder(ostrm, m_session.MessageContext);
            TrustListDataType trustList = new TrustListDataType();
            trustList.Decode(decoder);
            decoder.Close();
            ostrm.Close();

            return trustList;
        }
        #endregion

        #region Private Methods
        private IUserIdentity ElevatePermissions()
        {
            IUserIdentity oldUser = m_session.Identity;

            if (m_adminCredentials == null || !Object.ReferenceEquals(m_session.Identity, m_adminCredentials))
            {
                IUserIdentity newCredentials = null;

                if (m_adminCredentials == null)
                {
                    var handle = AdminCredentialsRequired;

                    if (handle == null)
                    {
                        throw new InvalidOperationException("The operation requires administrator credentials.");
                    }

                    var args = new AdminCredentialsRequiredEventArgs();
                    handle(this, args);
                    newCredentials = args.Credentials;

                    if (args.CacheCredentials)
                    {
                        m_adminCredentials = args.Credentials;
                    }
                }
                else
                {
                    newCredentials = m_adminCredentials;
                }

                try
                {
                    m_session.UpdateSession(newCredentials, m_preferredLocales);
                }
                catch (Exception)
                {
                    m_adminCredentials = null;
                    throw;
                }
            }

            return oldUser;
        }

        private void RevertPermissions(IUserIdentity oldUser)
        {
            try
            {
                if (Object.ReferenceEquals(m_session.Identity, m_adminCredentials))
                {
                    m_session.UpdateSession(oldUser, m_preferredLocales);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Error reverting to normal permissions.");
            }
        }
        #endregion

        #region Private Fields
        private ApplicationInstance m_application;
        private string m_endpointUrl;
        private string[] m_preferredLocales;
        private Session m_session;
        private UserIdentity m_adminCredentials;
        #endregion
    }
}
