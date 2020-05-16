﻿using System;
using System.Buffers;
using System.Text;
using Uno.Disposables;

namespace Windows.Storage.Streams
{
	public sealed partial class DataReader : IDataReader, IDisposable
	{
		private readonly static ArrayPool<byte> _pool = ArrayPool<byte>.Create();
		private readonly IBuffer _buffer;

		private int _bufferPosition = 0;

		private DataReader(IBuffer buffer)
		{
			_buffer = buffer;
		}

		public static DataReader FromBuffer(IBuffer buffer) => new DataReader(buffer);

		public UnicodeEncoding UnicodeEncoding { get; set; }

		public ByteOrder ByteOrder { get; set; }

		public uint UnconsumedBufferLength => (uint)(_buffer.Length - _bufferPosition);

		public byte ReadByte()
		{
			VerifyRead(1);
			return ReadByteFromBuffer();
		}

		public void ReadBytes(byte[] value)
		{
			VerifyRead(value.Length);
			ReadBytesFromBuffer(value);
		}

		public IBuffer ReadBuffer(uint length)
		{
			VerifyRead((int)length);
			var bufferData = new byte[length];
			ReadBytes(bufferData);
			return new Buffer(bufferData);
		}

		public DateTimeOffset ReadDateTime()
		{
			long ticks = ReadInt64();
			var date = new DateTime(1601, 1, 1, 0, 0, 0).ToLocalTime();
			date = date.AddTicks(ticks);

			return date;
		}

		public bool ReadBoolean()
		{
			VerifyRead(1);
			var nextByte = ReadByteFromBuffer();
			return nextByte != 0;
		}

		public Guid ReadGuid()
		{
			var u32 = ReadInt32();
			var u16a = ReadInt16();
			var u16b = ReadInt16();
			using (RentArray(8, out var u64))
			{
				ReadBytesFromBuffer(u64);

				return new Guid(u32, u16a, u16b, u64);
			}
		}

		public short ReadInt16()
		{
			VerifyRead(2);
			using (RentArray(2, out var bytes))
			{
				ReadChunkFromBuffer(bytes);
				return BitConverter.ToInt16(bytes, 0);
			}
		}

		public int ReadInt32()
		{
			VerifyRead(4);
			using (RentArray(4, out var bytes))
			{
				ReadChunkFromBuffer(bytes);
				return BitConverter.ToInt32(bytes, 0);				
			}
		}

		public long ReadInt64()
		{
			VerifyRead(8);
			using (RentArray(8, out var bytes))
			{
				ReadChunkFromBuffer(bytes);
				return BitConverter.ToInt64(bytes, 0);				
			}
		}

		public ushort ReadUInt16()
		{
			VerifyRead(2);
			using (RentArray(2, out var bytes))
			{
				ReadChunkFromBuffer(bytes);
				return BitConverter.ToUInt16(bytes, 0);
			}
		}

		public uint ReadUInt32()
		{
			VerifyRead(4);
			using (RentArray(4, out var bytes))
			{
				ReadChunkFromBuffer(bytes);
				return BitConverter.ToUInt32(bytes, 0);
			}
		}

		public ulong ReadUInt64()
		{
			VerifyRead(8);
			using (RentArray(8, out var bytes))
			{
				ReadChunkFromBuffer(bytes);
				return BitConverter.ToUInt64(bytes, 0);
			}
		}

		public float ReadSingle()
		{
			VerifyRead(4);
			using (RentArray(4, out var bytes))
			{
				ReadChunkFromBuffer(bytes);
				var value = BitConverter.ToSingle(bytes, 0);
				return value;
			}
		}

		public double ReadDouble()
		{
			VerifyRead(8);
			using (RentArray(8, out var bytes))
			{
				ReadChunkFromBuffer(bytes);
				return BitConverter.ToDouble(bytes, 0);
			}
		}

		public string ReadString(uint codeUnitCount)
		{
			// although docs says that input parameter is "codeUnitCount", sample
			// https://docs.microsoft.com/en-us/uwp/api/Windows.Storage.Streams.DataReader?view=winrt-19041
			// shows that it is BYTE count, not CODEUNIT count.
			// codepoint in UTF-8 can be encoded in anything from 1 to 6 bytes.

			int length = (int)codeUnitCount;
			VerifyRead(length);

			if (!(_buffer is Buffer buffer))
			{
				throw new NotSupportedException("This type of buffer is not supported.");
			}

			string result;
			switch (UnicodeEncoding)
			{
				case UnicodeEncoding.Utf8:
					result = Encoding.UTF8.GetString(buffer.Data, _bufferPosition, length);
					break;
				case UnicodeEncoding.Utf16LE:
					result = Encoding.Unicode.GetString(buffer.Data, _bufferPosition, length);
					break;
				case UnicodeEncoding.Utf16BE:
					result = Encoding.BigEndianUnicode.GetString(buffer.Data, _bufferPosition, length);
					break;
				default:
					throw new InvalidOperationException("Unsupported UnicodeEncoding value.");
			}
			_bufferPosition += length;
			return result;
		}

		public TimeSpan ReadTimeSpan() => TimeSpan.FromTicks(ReadInt64());

		public IBuffer DetachBuffer() => _buffer;

		public void Dispose()
		{
		}

		private void VerifyRead(int count)
		{
			if (_bufferPosition + count >= _buffer.Length)
			{
				throw new InvalidOperationException($"Buffer too short to accomodate for {count} requested bytes.");
			}
		}

		private byte ReadByteFromBuffer()
		{
			byte nextByte;
			switch (_buffer)
			{
				case Buffer buffer:
					nextByte = buffer.Data[_bufferPosition];
					break;
				default:
					throw new NotSupportedException("This buffer is not supported");
			}
			return nextByte;
		}

		private void ReadBytesFromBuffer(byte[] data)
		{
			switch (_buffer)
			{
				case Buffer buffer:
					Array.Copy(buffer.Data, _bufferPosition, data, 0, data.Length);
					break;
				default:
					throw new NotSupportedException("This buffer is not supported");
			}
			_bufferPosition += data.Length;
		}

		private void ReadChunkFromBuffer(byte[] chunk)
		{
			var reverseOrder =
				(ByteOrder == ByteOrder.BigEndian && BitConverter.IsLittleEndian) ||
				(ByteOrder == ByteOrder.LittleEndian && !BitConverter.IsLittleEndian);

			ReadBytesFromBuffer(chunk);			
			_bufferPosition += chunk.Length;

			if (reverseOrder)
			{
				Array.Reverse(chunk);
			}
		}

		private IDisposable RentArray(int length, out byte[] rentedArray)
		{
			var poolArray = _pool.Rent(length);
			rentedArray = poolArray;
			return Disposable.Create(() => _pool.Return(poolArray));
		}
	}
}
