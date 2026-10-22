# Prints which logical processors share a physical core (GetLogicalProcessorInformationEx, RelationProcessorCore).
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class Topo {
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetLogicalProcessorInformationEx(int rel, IntPtr buf, ref int len);
  public static string Cores() {
    int len = 0; GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref len);
    IntPtr b = Marshal.AllocHGlobal(len); var s = new System.Text.StringBuilder();
    try {
      if (!GetLogicalProcessorInformationEx(0, b, ref len)) throw new System.ComponentModel.Win32Exception();
      for (int off = 0, core = 0; off < len; core++) {
        int size = Marshal.ReadInt32(b, off + 4);
        long mask = Marshal.ReadInt64(b, off + 32); // header 8 + PROCESSOR_RELATIONSHIP Flags,EffClass,Reserved[20],GroupCount(2) = 24 -> GroupMask[0].Mask
        s.AppendFormat("physical core {0}: logical mask 0x{1:X}\n", core, mask); off += size;
      }
    } finally { Marshal.FreeHGlobal(b); }
    return s.ToString();
  }
}
'@
[Topo]::Cores()
