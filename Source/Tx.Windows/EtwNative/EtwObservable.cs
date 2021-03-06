﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading;

namespace Tx.Windows
{
    /// <summary>
    ///     Factory for creating raw ETW (Event Tracing for Windows) observable sources
    /// </summary>
    public static class EtwObservable
    {
        /// <summary>
        ///     Creates a reader for one or more .etl (Event Trace Log) files
        /// </summary>
        /// <param name="etlFiles">up to 63 files to read</param>
        /// <returns>sequence of events ordered by timestamp</returns>
        public static IObservable<EtwNativeEvent> FromFiles(params string[] etlFiles)
        {
            if (etlFiles == null)
                throw new ArgumentNullException("etlFiles");

            if (etlFiles.Length == 0 || etlFiles.Length > 63)
                throw new ArgumentException("the supported count of files is from 1 to 63");

            return new NonDetachObservable<EtwNativeEvent>(o => new EtwFileReader(o, etlFiles));
        }

        /// <summary>
        ///     Creates a listener to ETW real-time session
        /// </summary>
        /// <param name="sessionName">session name</param>
        /// <returns>events received from the session</returns>
        public static IObservable<EtwNativeEvent> FromSession(string sessionName)
        {
            if (sessionName == null)
                throw new ArgumentNullException("sessionName");

            return new NonDetachObservable<EtwNativeEvent>(o => new EtwListener(o, sessionName));
        }

        /// <summary>
        ///     Extracts manifest from .etl file that was produced using System.Diagnostics.Tracing.EventSource
        /// </summary>
        /// <param name="etlFile">Trace file</param>
        /// <returns></returns>
        public static string[] ExtractManifests(string etlFile)
        {
            IObservable<EtwNativeEvent> all = FromFiles(etlFile);

            var manifests = new SortedSet<string>();
            var sb = new StringBuilder();
            var evt = new ManualResetEvent(false);

            IDisposable d = all.Subscribe(e =>
                {
                    if (e.Id != 0xfffe) // 65534
                    {
                        return;
                    }

                    byte format = e.ReadByte();
                    if (format != 1)
                        throw new Exception("Unsuported manifest format found in EventSource event" + format);

                    byte majorVersion = e.ReadByte();
                    byte minorVersion = e.ReadByte();
                    byte magic = e.ReadByte();
                    if (magic != 0x5b)
                        throw new Exception("Unexpected content in EventSource event that was supposed to have manifest");

                    ushort totalChunks = e.ReadUInt16();
                    ushort chunkNumber = e.ReadUInt16();

                    string chunk = e.ReadAnsiString();
                    sb.Append(chunk);

                    if (chunkNumber == totalChunks - 1)
                    {
                        string manifest = sb.ToString();
                        sb = new StringBuilder();

                        if (!manifests.Contains(manifest))
                        {
                            manifests.Add(manifest);
                        }
                    }
                },
                                          () => evt.Set());

            evt.WaitOne();
            d.Dispose();

            return new List<string>(manifests).ToArray();
        }
    }
}