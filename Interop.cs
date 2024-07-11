namespace Antmicro.Renode.Plugins.Auxon.Interop;

using System;
using System.Runtime.InteropServices;

[Serializable]
public class ModalityException : Exception
{
    public ModalityException(int code) : base(String.Format("Modality error code {0}", code)) { }
}

class ModalityRuntime : IDisposable
{
    [DllImport("libmodality.so")]
    private static extern int modality_runtime_new(out IntPtr handle);

    [DllImport("libmodality.so")]
    private static extern void modality_runtime_free(IntPtr handle);

    public ModalityRuntime()
    {
        IntPtr handle;
        int res = modality_runtime_new(out handle);
        if (res != 0)
        {
            throw new ModalityException(res);
        }
        this.handle = handle;
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            modality_runtime_free(this.handle);
            this.disposed = true;
        }
    }

    internal IntPtr handle;
    private bool disposed;
}

class ModalityIngestClient : IDisposable
{
    [DllImport("libmodality.so")]
    private static extern int modality_ingest_client_new(IntPtr runtime, out IntPtr handle);

    [DllImport("libmodality.so")]
    private static extern void modality_ingest_client_free(IntPtr handle);

    [DllImport("libmodality.so")]
    private static extern int modality_ingest_client_connect(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string url,
        int allow_insecure_tls
    );

    [DllImport("libmodality.so")]
    private static extern void modality_ingest_client_flush(IntPtr handle);

    [DllImport("libmodality.so")]
    private static extern int modality_ingest_client_authenticate(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string auth_token
    );

    [DllImport("libmodality.so")]
    private static extern int modality_ingest_client_declare_attr_key(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key_name,
        out UInt32 interned_attr_key
    );

    [DllImport("libmodality.so")]
    private static extern int modality_ingest_client_open_timeline(IntPtr handle, ref TimelineId id);

    [DllImport("libmodality.so")]
    private static extern int modality_ingest_client_close_timeline(IntPtr handle);

    [DllImport("libmodality.so")]
    private static extern int modality_ingest_client_timeline_metadata(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPArray)] InteropAttr[] attrs,
        UIntPtr attr_len
    );

    [DllImport("libmodality.so")]
    private static extern int modality_ingest_client_event(
        IntPtr handle,
        UInt64 ordering_lower,
        UInt64 ordering_upper,
        [MarshalAs(UnmanagedType.LPArray)] InteropAttr[] attrs,
        UIntPtr attr_len
    );

    public ModalityIngestClient(ModalityRuntime runtime)
    {
        IntPtr handle;
        int res = modality_ingest_client_new(runtime.handle, out handle);
        if (res != 0)
        {
            throw new ModalityException(res);
        }
        this.handle = handle;
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            modality_ingest_client_free(this.handle);
            this.disposed = true;
        }
    }

    public void Connect(string url, bool allow_insecure_tls = false)
    {
        int allow_insecure_int = 0;
        if (allow_insecure_tls)
        {
            allow_insecure_int = 1;
        }

        int res = modality_ingest_client_connect(this.handle, url, allow_insecure_int);
        if (res != 0)
        {
            throw new ModalityException(res);
        }
    }

    public void Authenticate(string token)
    {
        int res = modality_ingest_client_authenticate(this.handle, token);
        if (res != 0)
        {
            throw new ModalityException(res);
        }
    }

    public UInt32 DeclareAttrKey(string key)
    {
        UInt32 interned;
        int res = modality_ingest_client_declare_attr_key(this.handle, key, out interned);
        if (res != 0)
        {
            throw new ModalityException(res);
        }

        return interned;
    }

    public void OpenTimeline(TimelineId timelineId)
    {
        int res = modality_ingest_client_open_timeline(this.handle, ref timelineId);
        if (res != 0)
        {
            throw new ModalityException(res);
        }
    }

    public void CloseTimeline()
    {
        int res = modality_ingest_client_close_timeline(this.handle);
        if (res != 0)
        {
            throw new ModalityException(res);
        }
    }

    public void TimelineMetadata(AttrKVs attrs)
    {
        InteropAttr[] interopAttrs;
        using (var mem = attrs.PrepareForInterop(out interopAttrs))
        {
            int res = modality_ingest_client_timeline_metadata(this.handle, interopAttrs, (UIntPtr)interopAttrs.Length);
            if (res != 0)
            {
                throw new ModalityException(res);
            }
        }
    }

    public void Event(UInt64 ordering_lower, UInt64 ordering_upper, AttrKVs attrs)
    {
        InteropAttr[] interopAttrs;
        using (var mem = attrs.PrepareForInterop(out interopAttrs))
        {
            int res = modality_ingest_client_event(
                this.handle,
                ordering_lower, ordering_upper,
                interopAttrs, (UIntPtr)interopAttrs.Length
            );
            if (res != 0)
            {
                throw new ModalityException(res);
            }
        }
    }

    private IntPtr handle;
    private bool disposed;
}


[StructLayout(LayoutKind.Sequential)]
public struct TimelineId
{
    [DllImport("libmodality.so")]
    private static extern int modality_timeline_id_init(
        ref TimelineId id
    );

    public static TimelineId Allocate()
    {
        var tl = new TimelineId();
        tl.bytes = new byte[16];
        modality_timeline_id_init(ref tl);
        return tl;
    }

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    byte[] bytes;
}

[StructLayout(LayoutKind.Explicit)]
struct InteropAttr
{
    [FieldOffset(0)]
    internal UInt32 key;

    [FieldOffset(8)]
    internal InteropAttrVal val;

    public InteropAttr(uint key, InteropAttrVal val)
    {
        this.key = key;
        this.val = val;
    }
}

class AttrKVs
{
    public AttrKVs()
    {
        kvs = new List<(UInt32, object)>();
    }

    public void Add(UInt32 key, string val)
    {
        kvs.Add((key, val));
    }

    public void Add(UInt32 key, int val)
    {
        kvs.Add((key, val));
    }

    public void Add(UInt32 key, ulong val)
    {
        kvs.Add((key, val));
    }

    public void Add(UInt32 key, Nanoseconds val)
    {
        kvs.Add((key, val));
    }


    public HeapAllocations PrepareForInterop(out InteropAttr[] attrs)
    {
        var mem = new HeapAllocations();
        var attrsList = new List<InteropAttr>(kvs.Count);
        foreach ((var k, var v) in kvs)
        {
            if (v == null)
            {
                continue;
            }
            else if (v is Nanoseconds)
            {
                attrsList.Add(new InteropAttr(k, InteropAttrVal.Timestamp((Nanoseconds)v)));
            }
            else if (v is string)
            {
                attrsList.Add(new InteropAttr(k, InteropAttrVal.String(mem.UTF8String((string)v))));
            }
            else if (v is int)
            {
                attrsList.Add(new InteropAttr(k, InteropAttrVal.Integer((int)v)));
            }
            else if (v is ulong)
            {
                // TODO handle i64/u64 overflow
                attrsList.Add(new InteropAttr(k, InteropAttrVal.Integer(((Int64)(ulong)v))));
            }
            else if (v is double)
            {
                attrsList.Add(new InteropAttr(k, InteropAttrVal.Float((double)v)));
            }
            else if (v is bool)
            {
                attrsList.Add(new InteropAttr(k, InteropAttrVal.Bool((bool)v)));
            }
            else
            {
                throw new Exception("Value can't be converted to an AttrVal:" + v.GetType());
            }
        }

        attrs = attrsList.ToArray();
        return mem;
    }

    internal readonly List<(UInt32, object)> kvs;
}

class HeapAllocations : IDisposable
{
    public HeapAllocations()
    {
        this.allocations = new List<IntPtr>();
        this.disposed = false;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            foreach (var p in allocations)
            {
                Marshal.FreeCoTaskMem(p);
            }
        }
    }

    public IntPtr UTF8String(string s)
    {
        var p = Marshal.StringToCoTaskMemUTF8(s);
        this.allocations.Add(p);
        return p;
    }

    public void Add(IntPtr p)
    {
        this.allocations.Add(p);
    }

    private readonly List<IntPtr> allocations;
    private readonly bool disposed;

}

[StructLayout(LayoutKind.Explicit)]
struct InteropAttrVal
{
    [DllImport("libmodality.so")]
    private static extern int modality_attr_val_set_timeline_id(ref InteropAttrVal attrVal, ref TimelineId timelineId);

    [DllImport("libmodality.so")]
    private static extern int modality_attr_val_set_string(ref InteropAttrVal attrVal, IntPtr val);

    [DllImport("libmodality.so")]
    private static extern int modality_attr_val_set_integer(ref InteropAttrVal attrVal, Int64 val);

    [DllImport("libmodality.so")]
    private static extern int modality_attr_val_set_float(ref InteropAttrVal attrVal, double val);

    [DllImport("libmodality.so")]
    private static extern int modality_attr_val_set_bool(ref InteropAttrVal attrVal, bool val);

    [DllImport("libmodality.so")]
    private static extern int modality_attr_val_set_timestamp(ref InteropAttrVal attrVal, UInt64 val);

    public static InteropAttrVal Timestamp(Nanoseconds ns)
    {
        var av = new InteropAttrVal();
        av.tag = InteropAttrValTag.Timestamp;
        av.value = IntPtr.Zero;

        int res = modality_attr_val_set_timestamp(ref av, ns.nanos);
        if (res != 0)
        {
            throw new ModalityException(res);
        }

        return av;
    }

    public static InteropAttrVal String(IntPtr s)
    {
        var av = new InteropAttrVal();
        av.tag = InteropAttrValTag.String;
        av.value = IntPtr.Zero;

        int res = modality_attr_val_set_string(ref av, s);
        if (res != 0)
        {
            throw new ModalityException(res);
        }

        return av;
    }

    public static InteropAttrVal Integer(Int64 i)
    {
        var av = new InteropAttrVal();
        av.tag = InteropAttrValTag.Integer;
        av.value = IntPtr.Zero;

        int res = modality_attr_val_set_integer(ref av, i);
        if (res != 0)
        {
            throw new ModalityException(res);
        }

        return av;
    }

    public static InteropAttrVal Float(double f)
    {
        var av = new InteropAttrVal();
        av.tag = InteropAttrValTag.Float;
        av.value = IntPtr.Zero;

        int res = modality_attr_val_set_float(ref av, f);
        if (res != 0)
        {
            throw new ModalityException(res);
        }

        return av;
    }

    public static InteropAttrVal Bool(bool b)
    {
        var av = new InteropAttrVal();
        av.tag = InteropAttrValTag.Bool;
        av.value = IntPtr.Zero;

        int res = modality_attr_val_set_bool(ref av, b);
        if (res != 0)
        {
            throw new ModalityException(res);
        }

        return av;
    }

    [FieldOffset(0)]
    internal InteropAttrValTag tag;

    [FieldOffset(8)]
    internal IntPtr value;
}

enum InteropAttrValTag
{
    TimelineId = 0,
    String,
    Integer,
    BigInt,
    Float,
    Bool,
    Timestamp,
    LogicalTime
}

public class Nanoseconds
{
    internal UInt64 nanos;

    public Nanoseconds(ulong nanos)
    {
        this.nanos = nanos;
    }
}
