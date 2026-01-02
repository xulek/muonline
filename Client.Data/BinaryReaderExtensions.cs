using System.Runtime.InteropServices;
using System.Text;

namespace Client.Data
{
    public static class BinaryReaderExtensions
    {
        public static string ReadString(this BinaryReader br, int length)
        {
            var buff = br.ReadBytes(length);
            var idx = Array.IndexOf(buff, (byte)0);
            return Encoding.ASCII.GetString(buff, 0, idx >= 0 ? idx : buff.Length);
        }

        public static T ReadStruct<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(this BinaryReader br) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] data = br.ReadBytes(size);
            if (data.Length < size) throw new EndOfStreamException();

            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }

        public static T[] ReadStructArray<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(this BinaryReader br, int length) where T : struct
        {
            if (length <= 0) return Array.Empty<T>();
            var structs = new T[length];
            for (int i = 0; i < length; i++)
            {
                structs[i] = br.ReadStruct<T>();
            }
            return structs;
        }
    }
}
