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
            if (!retainNegativeEvidence)
            {
                var legacyLabelId = ReadValue(fieldValues, "_IpLabelId");
                if (string.IsNullOrWhiteSpace(legacyLabelId))
                {
                    return null;
                }
                return new ListDocumentInformationProtectionSnapshot
                {
                    LabelId = legacyLabelId,
                    AssignmentMethod = ReadValue(fieldValues, "_IpLabelAssignmentMethod"),
                    HasUserDefinedProtection = ReadValue(fieldValues, "_HasUserDefinedProtection"),
                    OwnerEmail = ReadValue(fieldValues, "_IpLabelOwnerEmail"),
                    LabelHash = ReadValue(fieldValues, "_IpLabelHash"),
                    PromotionCtagVersion = ReadValue(fieldValues, "_IpLabelPromotionCtagVersion"),
                    DecryptSkipReason = ReadMetaInfoValue(fieldValues, "vti_decryptskipreason")
                };
            }

            var labelObserved = TryReadValue(fieldValues, "_IpLabelId", out var labelId);
            var userDefinedObserved = TryReadValue(
                fieldValues,
                "_HasUserDefinedProtection",
                out var hasUserDefinedProtection);
            var encryptedObserved = TryReadValue(
                fieldValues,
                "_HasEncryptedContent",
                out var hasEncryptedContent);
            var rmsTemplateObserved = TryReadValue(
                fieldValues,
                "_RmsTemplateId",
                out var rmsTemplateId);
            var decryptSkipObserved = TryReadMetaInfoValue(
                fieldValues,
                "vti_decryptskipreason",
                out var decryptSkipReason);
            if (!labelObserved
                && !userDefinedObserved
                && !encryptedObserved
                && !rmsTemplateObserved
                && !decryptSkipObserved)
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
                DecryptSkipReason = decryptSkipReason,
                HasEncryptedContent = hasEncryptedContent,
                RmsTemplateId = rmsTemplateId,
                LabelFieldObserved = labelObserved,
                UserDefinedProtectionFieldObserved = userDefinedObserved,
                DecryptSkipReasonObserved = decryptSkipObserved,
                HasEncryptedContentFieldObserved = encryptedObserved,
                RmsTemplateIdFieldObserved = rmsTemplateObserved
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

        private static string ReadMetaInfoValue(
            IDictionary<string, object> fieldValues,
            string propertyName)
        {
            TryReadMetaInfoValue(fieldValues, propertyName, out var result);
            return result;
        }

        private static bool TryReadMetaInfoValue(
            IDictionary<string, object> fieldValues,
            string propertyName,
            out string result)
        {
            result = null;
            if (!TryReadValue(fieldValues, "MetaInfo", out var metaInfo))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(metaInfo))
            {
                return true;
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
                result = line.Substring(separator + 1);
                return true;
            }
            return true;
        }
    }
}
