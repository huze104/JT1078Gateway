﻿using DotNetty.Buffers;
using DotNetty.Codecs.Http.WebSockets;
using JT1078.DotNetty.Core.Session;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JT1078.Protocol;
using System.Collections.Concurrent;
using JT1078.Protocol.Enums;
using System.Diagnostics;
using System.IO.Pipes;
using Newtonsoft.Json;
using DotNetty.Common.Utilities;
using DotNetty.Codecs.Http;
using DotNetty.Handlers.Streams;
using DotNetty.Transport.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using JT1078.DotNetty.Core.Extensions;

namespace JT1078.DotNetty.TestHosting
{
    class FFMPEGHTTPFLVHostedService : IHostedService,IDisposable
    {
        private readonly Process process;
        private readonly NamedPipeServerStream pipeServerOut;
        private const string PipeNameOut = "demo1serverout";

        private readonly JT1078HttpSessionManager jT1078HttpSessionManager;

        /// <summary>
        /// 需要缓存flv的第一包数据，当新用户进来先推送第一包的数据
        /// </summary>
        private byte[] flvFirstPackage;
       
        private ConcurrentDictionary<string, byte> exists = new ConcurrentDictionary<string, byte>();
        public FFMPEGHTTPFLVHostedService(JT1078HttpSessionManager jT1078HttpSessionManager)
        {
            pipeServerOut = new NamedPipeServerStream(PipeNameOut, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,10240,10240);
            process = new Process
            {
                StartInfo =
                {
                    FileName = @"C:\ffmpeg\bin\ffmpeg.exe",
                    Arguments = $@"-f dshow -i video={HardwareCamera.CameraName} -c copy -f flv -vcodec h264 -y \\.\pipe\{PipeNameOut}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            this.jT1078HttpSessionManager = jT1078HttpSessionManager;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            process.Start();
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        Console.WriteLine("IsConnected>>>" + pipeServerOut.IsConnected);
                        if (pipeServerOut.IsConnected)
                        {
                            if (pipeServerOut.CanRead)
                            {
                                Span<byte> v1 = new byte[2048];
                                var length = pipeServerOut.Read(v1);
                                var realValue = v1.Slice(0, length).ToArray();
                                if (realValue.Length <= 0) continue;
                                if (flvFirstPackage == null)
                                {
                                    flvFirstPackage = realValue;
                                }
                                if (jT1078HttpSessionManager.GetAll().Count() > 0)
                                {
                                    foreach (var session in jT1078HttpSessionManager.GetAll())
                                    {
                                        if (!exists.ContainsKey(session.Channel.Id.AsShortText()))
                                        {
                                            session.SendHttpFirstChunkAsync(flvFirstPackage);
                                            exists.TryAdd(session.Channel.Id.AsShortText(), 0);
                                        }
                                        session.SendHttpOtherChunkAsync(realValue);
                                    }
                                }
                                //Console.WriteLine(JsonConvert.SerializeObject(realValue)+"-"+ length.ToString());
                            }
                        }
                        else
                        {
                            if (!pipeServerOut.IsConnected)
                            {
                                Console.WriteLine("WaitForConnection Star...");
                                pipeServerOut.WaitForConnectionAsync().Wait(300);
                                Console.WriteLine("WaitForConnection End...");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
            });
            return Task.CompletedTask;
        }
        public void Dispose()
        {
            pipeServerOut.Dispose();
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                process.Kill();
                pipeServerOut.Flush();
                pipeServerOut.Close();
            }
            catch
            {


            }
            return Task.CompletedTask;
        }
    }
}
