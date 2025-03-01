//
// Copyright (C) 2015 crosire & contributors
// License: https://github.com/crosire/scripthookvdotnet#license
//

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Collections.Generic;

namespace RDR2DN
{
	/// <summary>
	/// Class responsible for executing script functions.
	/// </summary>
	public static unsafe class NativeFunc
	{
		#region ScriptHookRDR Imports
		/// <summary>
		/// Initializes the stack for a new script function call.
		/// </summary>
		/// <param name="hash">The function hash to call.</param>
		[DllImport("ScriptHookRDR2.dll", ExactSpelling = true, EntryPoint = "?nativeInit@@YAX_K@Z")]
		static extern void NativeInit(ulong hash);

		/// <summary>
		/// Pushes a function argument on the script function stack.
		/// </summary>
		/// <param name="val">The argument value.</param>
		[DllImport("ScriptHookRDR2.dll", ExactSpelling = true, EntryPoint = "?nativePush64@@YAX_K@Z")]
		static extern void NativePush64(ulong val);

		/// <summary>
		/// Executes the script function call.
		/// </summary>
		/// <returns>A pointer to the return value of the call.</returns>
		[DllImport("ScriptHookRDR2.dll", ExactSpelling = true, EntryPoint = "?nativeCall@@YAPEA_KXZ")]
		static unsafe extern ulong* NativeCall();
		#endregion

		/// <summary>
		/// Internal script task which holds all data necessary for a script function call.
		/// </summary>
		public class NativeTask : IScriptTask
		{
            public ulong Hash;
            public ulong[] Arguments;
            public unsafe ulong* Result;

			public void Run()
			{
				Result = InvokeInternal(Hash, Arguments);
			}
		}

		/// <summary>
		/// Pushes a single string component on the text stack.
		/// </summary>
		/// <param name="str">The string to push.</param>
		static void PushString(string str)
		{
			
			var domain = RDR2DN.ScriptDomain.CurrentDomain;
			if (domain == null)
			{
				throw new InvalidOperationException("Illegal scripting call outside script domain.");
			}

            ulong[] conargs = ConvertPrimitiveArguments(new object[] { 10, "LITERAL_STRING", str });
            domain.ExecuteTask(new NativeTask {
				Hash = 0xFA925AC00EB830B9,
				Arguments = conargs
			});
		}

		public static void PushLongString(string str)
		{
			PushLongString(str, PushString);
		}
		public static void PushLongString(string str, Action<string> action)
		{
			const int maxLengthUtf8 = 99;

			if (Encoding.UTF8.GetByteCount(str) <= maxLengthUtf8)
			{
				action(str);
				return;
			}

			int startPos = 0;
			int currentPos = 0;
			int currentUtf8StrLength = 0;

			while (currentPos < str.Length)
			{
				int codePointSize = 0;

				// Calculate the UTF-8 code point size of the current character
				var chr = str[currentPos];
				if (chr < 0x80)
				{
					codePointSize = 1;
				}
				else if (chr < 0x800)
				{
					codePointSize = 2;
				}
				else if (chr < 0x10000)
				{
					codePointSize = 3;
				}
				else
				{
					#region Surrogate check
					const int LowSurrogateStart = 0xD800;
					const int HighSurrogateStart = 0xD800;

					var temp1 = (int)chr - HighSurrogateStart;
					if (temp1 >= 0 && temp1 <= 0x7ff)
					{
						// Found a high surrogate
						if (currentPos < str.Length - 1)
						{
							var temp2 = str[currentPos + 1] - LowSurrogateStart;
							if (temp2 >= 0 && temp2 <= 0x3ff)
							{
								// Found a low surrogate
								codePointSize = 4;
							}
						}
					}
					#endregion
				}

				if (currentUtf8StrLength + codePointSize > maxLengthUtf8)
				{
					action(str.Substring(startPos, currentPos - startPos));

					startPos = currentPos;
					currentUtf8StrLength = 0;
				}
				else
				{
					currentPos++;
					currentUtf8StrLength += codePointSize;
				}

				// Additional increment is needed for surrogate
				if (codePointSize == 4)
				{
					currentPos++;
				}
			}

			action(str.Substring(startPos, str.Length - startPos));
		}

		/// <summary>
		/// Helper function that converts an array of primitive values to a native stack.
		/// </summary>
		/// <param name="args"></param>
		/// <returns></returns>
		internal static ulong[] ConvertPrimitiveArguments(object[] args)
		{
			var result = new ulong[args.Length];
			for (int i = 0; i < args.Length; ++i)
			{
				if (args[i] is bool valueBool)
				{
					result[i] = valueBool ? 1ul : 0ul;
					continue;
				}
				if (args[i] is byte valueByte)
				{
					result[i] = (ulong)valueByte;
					continue;
				}
				if (args[i] is int valueInt32)
				{
					result[i] = (ulong)valueInt32;
					continue;
				}
				if (args[i] is ulong valueUInt64)
				{
					result[i] = valueUInt64;
					continue;
				}
				if (args[i] is float valueFloat)
				{
					result[i] = *(ulong*)&valueFloat;
					continue;
				}
				if (args[i] is IntPtr valueIntPtr)
				{
					result[i] = (ulong)valueIntPtr.ToInt64();
					continue;
				}
				if (args[i] is string valueString)
				{
					result[i] = (ulong)ScriptDomain.CurrentDomain.PinString(valueString).ToInt64();
					continue;
				}

				throw new ArgumentException("Unknown primitive type in native argument list", nameof(args));
			}

			return result;
		}

		/// <summary>
		/// Executes a script function inside the current script domain.
		/// </summary>
		/// <param name="hash">The function has to call.</param>
		/// <param name="args">A list of function arguments.</param>
		/// <returns>A pointer to the return value of the call.</returns>
		public static ulong* Invoke(ulong hash, params ulong[] args)
		{
			var domain = ScriptDomain.CurrentDomain;
			if (domain == null)
			{
				throw new InvalidOperationException("Illegal scripting call outside script domain.");
			}

			var task = new NativeTask { Hash = hash, Arguments = args };
			domain.ExecuteTask(task);

			return task.Result;
		}
		public static ulong* Invoke(ulong hash, params object[] args)
		{
			return Invoke(hash, ConvertPrimitiveArguments(args));
		}

		/// <summary>
		/// Executes a script function immediately. This may only be called from the main script domain thread.
		/// </summary>
		/// <param name="hash">The function has to call.</param>
		/// <param name="args">A list of function arguments.</param>
		/// <returns>A pointer to the return value of the call.</returns>
		public static ulong* InvokeInternal(ulong hash, params ulong[] args)
		{
			NativeInit(hash);
			foreach (var arg in args)
				NativePush64(arg);
			return NativeCall();
		}

		public static ulong* InvokeInternal(ulong hash, params object[] args)
		{
			return InvokeInternal(hash, ConvertPrimitiveArguments(args));
		}  
    }
}
