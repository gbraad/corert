// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

namespace System.Globalization
{
    public partial class CompareInfo
    {
        [NonSerialized]
        private Interop.Globalization.SafeSortHandle _sortHandle;

        [NonSerialized]
        private bool _isAsciiEqualityOrdinal;

        private void InitSort(CultureInfo culture)
        {
            _sortName = culture.SortName;

            if (_invariantMode)
            {
                _isAsciiEqualityOrdinal = true;
            }
            else
            {
                Interop.Globalization.ResultCode resultCode = Interop.Globalization.GetSortHandle(GetNullTerminatedUtf8String(_sortName), out _sortHandle);
                if (resultCode != Interop.Globalization.ResultCode.Success)
                {
                    _sortHandle.Dispose();

                    if (resultCode == Interop.Globalization.ResultCode.OutOfMemory)
                        throw new OutOfMemoryException();

                    throw new ExternalException(SR.Arg_ExternalException);
                }
                _isAsciiEqualityOrdinal = (_sortName == "en-US" || _sortName == "");
            }
        }

        internal static unsafe int IndexOfOrdinalCore(string source, string value, int startIndex, int count, bool ignoreCase)
        {
            Debug.Assert(!GlobalizationMode.Invariant);

            Debug.Assert(source != null);
            Debug.Assert(value != null);

            if (value.Length == 0)
            {
                return startIndex;
            }

            if (count < value.Length)
            {
                return -1;
            }

            if (ignoreCase)
            {
                fixed (char* pSource = source)
                {
                    int index = Interop.Globalization.IndexOfOrdinalIgnoreCase(value, value.Length, pSource + startIndex, count, findLast: false);
                    return index != -1 ?
                        startIndex + index :
                        -1;
                }
            }

            int endIndex = startIndex + (count - value.Length);
            for (int i = startIndex; i <= endIndex; i++)
            {
                int valueIndex, sourceIndex;

                for (valueIndex = 0, sourceIndex = i;
                     valueIndex < value.Length && source[sourceIndex] == value[valueIndex];
                     valueIndex++, sourceIndex++) ;

                if (valueIndex == value.Length)
                {
                    return i;
                }
            }

            return -1;
        }

        internal static unsafe int LastIndexOfOrdinalCore(string source, string value, int startIndex, int count, bool ignoreCase)
        {
            Debug.Assert(!GlobalizationMode.Invariant);

            Debug.Assert(source != null);
            Debug.Assert(value != null);

            if (value.Length == 0)
            {
                return startIndex;
            }

            if (count < value.Length)
            {
                return -1;
            }

            // startIndex is the index into source where we start search backwards from. 
            // leftStartIndex is the index into source of the start of the string that is 
            // count characters away from startIndex.
            int leftStartIndex = startIndex - count + 1;

            if (ignoreCase)
            {
                fixed (char* pSource = source)
                {
                    int lastIndex = Interop.Globalization.IndexOfOrdinalIgnoreCase(value, value.Length, pSource + leftStartIndex, count, findLast: true);
                    return lastIndex != -1 ?
                        leftStartIndex + lastIndex :
                        -1;
                }
            }

            for (int i = startIndex - value.Length + 1; i >= leftStartIndex; i--)
            {
                int valueIndex, sourceIndex;

                for (valueIndex = 0, sourceIndex = i;
                     valueIndex < value.Length && source[sourceIndex] == value[valueIndex];
                     valueIndex++, sourceIndex++) ;

                if (valueIndex == value.Length) {
                    return i;
                }
            }

            return -1;
        }

        private static unsafe int CompareStringOrdinalIgnoreCase(char* string1, int count1, char* string2, int count2)
        {
            Debug.Assert(!GlobalizationMode.Invariant);

            return Interop.Globalization.CompareStringOrdinalIgnoreCase(string1, count1, string2, count2);
        }

        // TODO https://github.com/dotnet/coreclr/issues/13827:
        // This method shouldn't be necessary, as we should be able to just use the overload
        // that takes two spans.  But due to this issue, that's adding significant overhead.
        private unsafe int CompareString(ReadOnlySpan<char> string1, string string2, CompareOptions options)
        {
            Debug.Assert(!_invariantMode);
            Debug.Assert(string2 != null);
            Debug.Assert((options & (CompareOptions.Ordinal | CompareOptions.OrdinalIgnoreCase)) == 0);

            fixed (char* pString1 = &MemoryMarshal.GetReference(string1))
            fixed (char* pString2 = &string2.GetRawStringData())
            {
                return Interop.Globalization.CompareString(_sortHandle, pString1, string1.Length, pString2, string2.Length, options);
            }
        }

        private unsafe int CompareString(ReadOnlySpan<char> string1, ReadOnlySpan<char> string2, CompareOptions options)
        {
            Debug.Assert(!_invariantMode);
            Debug.Assert((options & (CompareOptions.Ordinal | CompareOptions.OrdinalIgnoreCase)) == 0);

            fixed (char* pString1 = &MemoryMarshal.GetReference(string1))
            fixed (char* pString2 = &MemoryMarshal.GetReference(string2))
            {
                return Interop.Globalization.CompareString(_sortHandle, pString1, string1.Length, pString2, string2.Length, options);
            }
        }

        internal unsafe int IndexOfCore(string source, string target, int startIndex, int count, CompareOptions options, int* matchLengthPtr)
        {
            Debug.Assert(!_invariantMode);

            Debug.Assert(!string.IsNullOrEmpty(source));
            Debug.Assert(target != null);
            Debug.Assert((options & CompareOptions.OrdinalIgnoreCase) == 0);

            int index;

            if (target.Length == 0)
            {
                if(matchLengthPtr != null)
                    *matchLengthPtr = 0;
                return startIndex;
            }

            if (options == CompareOptions.Ordinal)
            {
                index = IndexOfOrdinal(source, target, startIndex, count, ignoreCase: false);
                if(index != -1)
                {
                    if(matchLengthPtr != null)
                        *matchLengthPtr = target.Length;
                }
                return index;
            }
#if CORECLR
            if (_isAsciiEqualityOrdinal && CanUseAsciiOrdinalForOptions(options) && source.IsFastSort() && target.IsFastSort())
            {
                index = IndexOf(source, target, startIndex, count, GetOrdinalCompareOptions(options));
                if(index != -1)
                {
                    if(matchLengthPtr != null)
                        *matchLengthPtr = target.Length;
                }
                return index;
            }
#endif
            fixed (char* pSource = source)
            {
                index = Interop.Globalization.IndexOf(_sortHandle, target, target.Length, pSource + startIndex, count, options, matchLengthPtr);

                return index != -1 ? index + startIndex : -1;
            }
        }

        private unsafe int LastIndexOfCore(string source, string target, int startIndex, int count, CompareOptions options)
        {
            Debug.Assert(!_invariantMode);

            Debug.Assert(!string.IsNullOrEmpty(source));
            Debug.Assert(target != null);
            Debug.Assert((options & CompareOptions.OrdinalIgnoreCase) == 0);

            if (target.Length == 0)
            {
                return startIndex;
            }

            if (options == CompareOptions.Ordinal)
            {
                return LastIndexOfOrdinalCore(source, target, startIndex, count, ignoreCase: false);
            }

#if CORECLR
            if (_isAsciiEqualityOrdinal && CanUseAsciiOrdinalForOptions(options) && source.IsFastSort() && target.IsFastSort())
            {
                return LastIndexOf(source, target, startIndex, count, GetOrdinalCompareOptions(options));
            }
#endif

            // startIndex is the index into source where we start search backwards from. leftStartIndex is the index into source
            // of the start of the string that is count characters away from startIndex.
            int leftStartIndex = (startIndex - count + 1);

            fixed (char* pSource = source)
            {
                int lastIndex = Interop.Globalization.LastIndexOf(_sortHandle, target, target.Length, pSource + (startIndex - count + 1), count, options);

                return lastIndex != -1 ? lastIndex + leftStartIndex : -1;
            }
        }

        private bool StartsWith(string source, string prefix, CompareOptions options)
        {
            Debug.Assert(!_invariantMode);

            Debug.Assert(!string.IsNullOrEmpty(source));
            Debug.Assert(!string.IsNullOrEmpty(prefix));
            Debug.Assert((options & (CompareOptions.Ordinal | CompareOptions.OrdinalIgnoreCase)) == 0);

#if CORECLR
            if (_isAsciiEqualityOrdinal && CanUseAsciiOrdinalForOptions(options) && source.IsFastSort() && prefix.IsFastSort())
            {
                return IsPrefix(source, prefix, GetOrdinalCompareOptions(options));
            }
#endif

            return Interop.Globalization.StartsWith(_sortHandle, prefix, prefix.Length, source, source.Length, options);
        }

        private bool EndsWith(string source, string suffix, CompareOptions options)
        {
            Debug.Assert(!_invariantMode);

            Debug.Assert(!string.IsNullOrEmpty(source));
            Debug.Assert(!string.IsNullOrEmpty(suffix));
            Debug.Assert((options & (CompareOptions.Ordinal | CompareOptions.OrdinalIgnoreCase)) == 0);

#if CORECLR
            if (_isAsciiEqualityOrdinal && CanUseAsciiOrdinalForOptions(options) && source.IsFastSort() && suffix.IsFastSort())
            {
                return IsSuffix(source, suffix, GetOrdinalCompareOptions(options));
            }
#endif

            return Interop.Globalization.EndsWith(_sortHandle, suffix, suffix.Length, source, source.Length, options);
        }
        
        private unsafe SortKey CreateSortKey(String source, CompareOptions options)
        {
            Debug.Assert(!_invariantMode);

            if (source==null) { throw new ArgumentNullException(nameof(source)); }

            if ((options & ValidSortkeyCtorMaskOffFlags) != 0)
            {
                throw new ArgumentException(SR.Argument_InvalidFlag, nameof(options));
            }
            
            byte [] keyData;
            if (source.Length == 0)
            { 
                keyData = Array.Empty<Byte>();
            }
            else
            {
                int sortKeyLength = Interop.Globalization.GetSortKey(_sortHandle, source, source.Length, null, 0, options);
                keyData = new byte[sortKeyLength];

                fixed (byte* pSortKey = keyData)
                {
                    Interop.Globalization.GetSortKey(_sortHandle, source, source.Length, pSortKey, sortKeyLength, options);
                }
            }

            return new SortKey(Name, source, options, keyData);
        }       

        private unsafe static bool IsSortable(char *text, int length)
        {
            Debug.Assert(!GlobalizationMode.Invariant);

            int index = 0;
            UnicodeCategory uc;

            while (index < length)
            {
                if (Char.IsHighSurrogate(text[index]))
                {
                    if (index == length - 1 || !Char.IsLowSurrogate(text[index+1]))
                        return false; // unpaired surrogate

                    uc = CharUnicodeInfo.GetUnicodeCategory(Char.ConvertToUtf32(text[index], text[index+1]));
                    if (uc == UnicodeCategory.PrivateUse || uc == UnicodeCategory.OtherNotAssigned)
                        return false;

                    index += 2;
                    continue;
                }

                if (Char.IsLowSurrogate(text[index]))
                {
                    return false; // unpaired surrogate
                }

                uc = CharUnicodeInfo.GetUnicodeCategory(text[index]);
                if (uc == UnicodeCategory.PrivateUse || uc == UnicodeCategory.OtherNotAssigned)
                {
                    return false;
                }

                index++;
            }

            return true;
        }

        // -----------------------------
        // ---- PAL layer ends here ----
        // -----------------------------

        internal unsafe int GetHashCodeOfStringCore(string source, CompareOptions options)
        {
            Debug.Assert(!_invariantMode);

            Debug.Assert(source != null);
            Debug.Assert((options & (CompareOptions.Ordinal | CompareOptions.OrdinalIgnoreCase)) == 0);

            if (source.Length == 0)
            {
                return 0;
            }

            int sortKeyLength = Interop.Globalization.GetSortKey(_sortHandle, source, source.Length, null, 0, options);

            // As an optimization, for small sort keys we allocate the buffer on the stack.
            if (sortKeyLength <= 256)
            {
                byte* pSortKey = stackalloc byte[sortKeyLength];
                Interop.Globalization.GetSortKey(_sortHandle, source, source.Length, pSortKey, sortKeyLength, options);
                return InternalHashSortKey(pSortKey, sortKeyLength);
            }

            byte[] sortKey = new byte[sortKeyLength];

            fixed (byte* pSortKey = &sortKey[0])
            {
                Interop.Globalization.GetSortKey(_sortHandle, source, source.Length, pSortKey, sortKeyLength, options);
                return InternalHashSortKey(pSortKey, sortKeyLength);
            }
        }

        private static unsafe int InternalHashSortKey(byte* sortKey, int sortKeyLength)
        {
            // TODO: Random hashing is yet to be done
            // Active Issue: https://github.com/dotnet/corert/issues/2588

            int hash1 = 5381;
            int hash2 = hash1;
            if (sortKeyLength == 0)
            {
                return 0;
            }
            if (sortKeyLength == 1)
            {
                return (((hash1 << 5) + hash1) ^ sortKey[0]) + (hash2 * 1566083941);
            }

            for (int i = 0; i < (sortKeyLength & ~1); i += 2) 
            {
                hash1 = ((hash1 << 5) + hash1) ^ sortKey[i];
                hash2 = ((hash2 << 5) + hash2) ^ sortKey[i+1];
            }
            return hash1 + (hash2 * 1566083941);
        }

        private static CompareOptions GetOrdinalCompareOptions(CompareOptions options)
        {
            if ((options & CompareOptions.IgnoreCase) == CompareOptions.IgnoreCase)
            {
                return CompareOptions.OrdinalIgnoreCase;
            }
            else
            {
                return CompareOptions.Ordinal;
            }
        }

        private static bool CanUseAsciiOrdinalForOptions(CompareOptions options)
        {
            // Unlike the other Ignore options, IgnoreSymbols impacts ASCII characters (e.g. ').
            return (options & CompareOptions.IgnoreSymbols) == 0;
        }

        private static byte[] GetNullTerminatedUtf8String(string s)
        {
            int byteLen = System.Text.Encoding.UTF8.GetByteCount(s);

            // Allocate an extra byte (which defaults to 0) as the null terminator.
            byte[] buffer = new byte[byteLen + 1];

            int bytesWritten = System.Text.Encoding.UTF8.GetBytes(s, 0, s.Length, buffer, 0);

            Debug.Assert(bytesWritten == byteLen);

            return buffer;
        }
        
        private SortVersion GetSortVersion()
        {
            Debug.Assert(!_invariantMode);

            int sortVersion = Interop.Globalization.GetSortVersion(_sortHandle);
            return new SortVersion(sortVersion, LCID, new Guid(sortVersion, 0, 0, 0, 0, 0, 0,
                                                             (byte) (LCID >> 24),
                                                             (byte) ((LCID  & 0x00FF0000) >> 16),
                                                             (byte) ((LCID  & 0x0000FF00) >> 8),
                                                             (byte) (LCID  & 0xFF)));
        }
    }
}
