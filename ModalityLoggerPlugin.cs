using System;
using Antmicro.Renode.Logging;
using Antmicro.Renode.UserInterface;
using Antmicro.Renode.Plugins;

using Antmicro.Renode.Plugins.Auxon.Interop;
using System.Runtime.InteropServices;

namespace Antmicro.Renode.Plugins.Auxon.ModalityLogger
{
    [Plugin(Name = "Modality Logger", Description = "Modality logger plugin", Version = "0.1", Vendor = "Auxon")]
    public class ModalityLoggerPlugin : IDisposable
    {
        public ModalityLoggerPlugin(Antmicro.Renode.UserInterface.Monitor monitor)
        {
            try
            {
                this.backend = new ModalityLoggerBackend();
                Logger.AddBackend(this.backend, "modality");
            }
            catch (Exception e)
            {
                Logger.Error("Unable to initialize Modality logger backend: " + e);
            }
        }

        public void Dispose()
        {
            if (this.backend != null)
            {
                Logger.RemoveBackend(this.backend);
                this.backend = null;
            }
        }

        private ILoggerBackend? backend;
    }

    class TimelineAttrKeys
    {
        public TimelineAttrKeys(ModalityIngestClient client)
        {
            this.name = client.DeclareAttrKey("timeline.name");
            this.runId = client.DeclareAttrKey("timeline.run_id");
            this.internalSource = client.DeclareAttrKey("timeline.internal.source");
        }

        public UInt32 name;
        public UInt32 runId;
        public UInt32 internalSource;
    }

    class EventAttrKeys
    {
        public EventAttrKeys(ModalityIngestClient client)
        {
            this.name = client.DeclareAttrKey("event.name");
            this.timestamp = client.DeclareAttrKey("event.timestamp");
            this.timestamp = client.DeclareAttrKey("event.timestamp");
            this.sourceId = client.DeclareAttrKey("event.source_id");
            this.threadId = client.DeclareAttrKey("event.thread_id");
            this.machineName = client.DeclareAttrKey("event.machine_name");
            this.objectName = client.DeclareAttrKey("event.object_name");
            this.logLevel = client.DeclareAttrKey("event.log_level");
        }

        public UInt32 name;
        public UInt32 timestamp;
        public UInt32 sourceId;
        public UInt32 threadId;
        public UInt32 machineName;
        public UInt32 objectName;
        public UInt32 logLevel;
    }

    public class ModalityLoggerBackend : LoggerBackend
    {
        public ModalityLoggerBackend()
        {
            this.disposed = false;
            this.runtime = new ModalityRuntime();
            this.ingest = new ModalityIngestClient(this.runtime);
            this.epochStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var ingest_url = GetEnv("MODALITY_INGEST_URL");
            if (ingest_url == null)
            {
                throw new Exception("MODALITY_INGEST_URL env var is not set");
            }

            var auth_token = GetEnv("MODALITY_AUTH_TOKEN");
            if (auth_token == null)
            {
                throw new Exception("MODALITY_AUTH_TOKEN env var is not set");
            }

            this.runId = GetEnv("MODALITY_RUN_ID") ?? Guid.NewGuid().ToString();

            this.ingest.Connect(ingest_url);
            this.ingest.Authenticate(auth_token);

            this.timeline_keys = new TimelineAttrKeys(this.ingest);
            this.event_keys = new EventAttrKeys(this.ingest);

            this.globalTimeline = new RenodeTimeline(TimelineId.Allocate(), null);
            this.currentTimeline = this.globalTimeline;
            this.machineTimelines = new Dictionary<string, RenodeTimeline>();

            var tlAttrs = new AttrKVs();
            tlAttrs.Add(timeline_keys.name, "RenodeGlobal");
            tlAttrs.Add(timeline_keys.runId, this.runId);
            tlAttrs.Add(timeline_keys.internalSource, "renode");
            this.ingest.OpenTimeline(this.globalTimeline.id);
            this.ingest.TimelineMetadata(tlAttrs);
        }

        [DllImport("libc.so.6")]
        private static extern IntPtr getenv([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        static string? GetEnv(string name)
        {
            var ptr = getenv(name);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.PtrToStringUTF8(ptr);
        }

        public override void Log(LogEntry entry)
        {
            if (!ShouldBeLogged(entry))
            {
                return;
            }

            SwitchToTimelineForMachineName(entry.MachineName);

            var attrs = new AttrKVs();
            attrs.Add(event_keys.name, entry.Message);
            attrs.Add(event_keys.logLevel, entry.Type.ToString());
            attrs.Add(event_keys.sourceId, entry.SourceId);
            attrs.Add(event_keys.sourceId, entry.SourceId);

            UInt64 epochNanos = ((UInt64)(entry.Time - this.epochStart).Ticks) * 100;
            attrs.Add(event_keys.timestamp, new Nanoseconds(epochNanos));

            if (entry.MachineName != null)
            {
                attrs.Add(event_keys.machineName, entry.MachineName);
            }

            if (entry.ThreadId != null)
            {
                attrs.Add(event_keys.threadId, (int)entry.ThreadId);
            }

            lock (ingest)
            {
                ingest.Event(entry.Id, 0, attrs);
            }
        }


        private void SwitchToTimelineForMachineName(string? machineName)
        {
            lock (this.currentTimeline)
            {
                if (this.currentTimeline.machineName == machineName)
                {
                    return;
                }

                if (machineName == null)
                {
                    this.currentTimeline = this.globalTimeline;
                    lock (ingest)
                    {
                        this.ingest.OpenTimeline(this.currentTimeline.id);
                    }
                    return;
                }

                RenodeTimeline? tl = null;
                var exists = machineTimelines.TryGetValue(machineName, out tl);
                if (exists && tl != null)
                {
                    // Switch to an existing timeline
                    this.currentTimeline = tl;
                    lock (ingest)
                    {
                        this.ingest.OpenTimeline(this.currentTimeline.id);
                    }
                    return;
                }

                // First time we've seen this MachineName; allocate a new timeline and send metadata
                tl = new RenodeTimeline(TimelineId.Allocate(), machineName);
                this.machineTimelines.Add(machineName, tl);
                this.currentTimeline = tl;

                var tlAttrs = new AttrKVs();
                tlAttrs.Add(timeline_keys.name, machineName);
                tlAttrs.Add(timeline_keys.runId, this.runId);
                tlAttrs.Add(timeline_keys.internalSource, "renode");

                lock (ingest)
                {
                    this.ingest.OpenTimeline(currentTimeline.id);
                    this.ingest.TimelineMetadata(tlAttrs);
                }
            }
        }

        public override void Dispose()
        {
            if (!this.disposed)
            {
                this.ingest.Dispose();
                this.runtime.Dispose();
                this.disposed = true;
            }
        }

        private bool disposed;
        private readonly ModalityRuntime runtime;
        private readonly ModalityIngestClient ingest;
        private readonly TimelineAttrKeys timeline_keys;
        private readonly EventAttrKeys event_keys;
        private readonly DateTime epochStart;

        private RenodeTimeline currentTimeline;
        private Dictionary<string, RenodeTimeline> machineTimelines;
        private RenodeTimeline globalTimeline;
        private string runId;
    }
}

class RenodeTimeline
{
    internal TimelineId id;
    internal string? machineName;

    public RenodeTimeline(TimelineId id, string? machineName)
    {
        this.id = id;
        this.machineName = machineName;
    }
}
