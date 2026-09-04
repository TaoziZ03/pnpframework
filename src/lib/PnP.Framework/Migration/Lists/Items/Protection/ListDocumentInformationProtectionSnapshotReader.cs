using System;
using System.Collections.Generic;

namespace PnP.Framework.Migration.Lists.Items.Protection
{
    internal static class ListDocumentInformationProtectionSnapshotReader
    {
        public static ListDocumentInformationProtectionSnapshot Read(
            IDictionary<string, object> fieldValues)
        {
            return Read(fieldValues, false);
        }

        public static ListDocumentInformationProtectionSnapshot Read(
            IDictionary<string, object> fieldValues,
            bool retainNegativeEvidence)
        {
            var labelObserved = TryReadValue(fieldValues, "_IpLabelId", out var labelId);
            var userDefinedObserved = TryReadValue(
                fieldValues,
                "_HasUserDefinedProtection",
                out var hasUserDefinedProtection);
            if (string.IsNullOrWhiteSpace(labelId)
                && !IsTrue(hasUserDefinedProtection)
                && (!retainNegativeEvidence || !labelObserved && !userDefinedObserved))
            {
                return null;
            }

            return new ListDocumentInformationProtectionSnapshot
            {
                LabelId = labelId,
                AssignmentMethod = ReadValue(fieldValues, "_IpLabelAssignmentMethod"),
                HasUserDefinedProtection = hasUserDefinedProtection,
                OwnerEmail = ReadValue(fieldValues, "_IpLabelOwnerEmail"),
                LabelHash = ReadValue(fieldValues, "_IpLabelHash"),
                PromotionCtagVersion = ReadValue(fieldValues, "_IpLabelPromotionCtagVersion"),
                DecryptSkipReason = ReadMetaInfoValue(fieldValues, "vti_decryptskipreason"),
                LabelFieldObserved = retainNegativeEvidence && labelObserved,
                UserDefinedProtectionFieldObserved = retainNegativeEvidence && userDefinedObserved
            };
        }

        private static bool TryReadValue(
            IDictionary<string, object> fieldValues,
            string internalName,
            out string result)
        {
            result = null;
            if (fieldValues == null)
            {
                return false;
            }
            foreach (var value in fieldValues)
            {
                if (string.Equals(value.Key, internalName, StringComparison.OrdinalIgnoreCase))
                {
                    result = value.Value == null ? null : Convert.ToString(value.Value);
                    return true;
                }
            }
            return false;
        }

        private static string ReadValue(IDictionary<string, object> fieldValues, string internalName)
        {
            return TryReadValue(fieldValues, internalName, out var result) ? result : null;
        }

        private static bool IsTrue(string value)
        {
            return bool.TryParse(value, out var parsed) && parsed
                || string.Equals(value, "1", StringComparison.Ordinal);
        }

        private static string ReadMetaInfoValue(
            IDictionary<string, object> fieldValues,
            string propertyName)
        {
            var metaInfo = ReadValue(fieldValues, "MetaInfo");
            if (string.IsNullOrWhiteSpace(metaInfo))
            {
                return null;
            }
            foreach (var line in metaInfo.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOf('|');
                var propertySeparator = line.IndexOf(':');
                if (separator <= propertySeparator || propertySeparator <= 0
                    || !string.Equals(
                        line.Substring(0, propertySeparator),
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                return line.Substring(separator + 1);
            }
            return null;
        }
    }
}
