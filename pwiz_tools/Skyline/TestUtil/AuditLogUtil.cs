﻿/*
 * Original author: Tobias Rohde <tobiasr .at. uw.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2018 University of Washington - Seattle, WA
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using pwiz.Common.SystemUtil;
using pwiz.Skyline.Controls.AuditLog;
using pwiz.Skyline.Model.AuditLog;
using pwiz.Skyline.Util.Extensions;

namespace pwiz.SkylineTestUtil
{
    public class AuditLogUtil
    {
        public class LogEntryMessages
        {
            public LogEntryMessages(LogMessage expectedUndoRedo, LogMessage expectedSummary, LogMessage[] expectedAllInfo, string extraInfo = null)
            {
                ExpectedUndoRedo = expectedUndoRedo;
                ExpectedSummary = expectedSummary;
                ExpectedAllInfo = expectedAllInfo;
                ExtraInfo = extraInfo;
            }

            public void AssertEquals(AuditLogEntry entry)
            {
                Assert.AreEqual(ExpectedUndoRedo, entry.UndoRedo);
                Assert.AreEqual(ExpectedSummary, entry.Summary);

                if (ExpectedAllInfo.Length != entry.AllInfo.Count)
                {
                    Assert.Fail("Expected: " +
                                string.Join(",\n", ExpectedAllInfo.Select(l => l.ToString())) +
                                "\nActual: " + string.Join(",\n", entry.AllInfo.Select(l => l.ToString())));
                }

                for (var i = 0; i < ExpectedAllInfo.Length; ++i)
                    Assert.AreEqual(ExpectedAllInfo[i], entry.AllInfo[i]);

                var expectedEmpty = string.IsNullOrEmpty(ExtraInfo);
                var actualEmpty = string.IsNullOrEmpty(entry.ExtraInfo);

                if (!expectedEmpty && !actualEmpty)
                    AssertEx.NoDiff(ExtraInfo, entry.ExtraInfo);
                else if (!expectedEmpty || !actualEmpty)
                    Assert.AreEqual(ExtraInfo, entry.ExtraInfo);
            }

            public LogMessage ExpectedUndoRedo { get; set; }
            public LogMessage ExpectedSummary { get; set; }
            public LogMessage[] ExpectedAllInfo { get; set; }

            public string ExtraInfo { get; private set; }
        }

        private static string LogMessageToCode(LogMessage msg, int indentLvl = 0)
        {
            var indent = "";
            for (var i = 0; i < indentLvl; ++i)
                indent += "    ";
            
            var detail = msg as DetailLogMessage;
            string reason = detail == null
                ? string.Empty
                : (string.IsNullOrEmpty(detail.Reason) ? "string.Empty, " : detail.Reason.Quote() + ",");
            var result = string.Format(indent + "new {0}(LogLevel.{1}, MessageType.{2}, SrmDocument.DOCUMENT_TYPE.{3}, {4}{5},\r\n",
                detail == null ? "LogMessage" : "DetailLogMessage",
                msg.Level, msg.Type, msg.DocumentType.ToString(), reason, msg.Expanded ? "true" : "false");
            foreach (var name in msg.Names)
            {
                var n = name.Replace("\"", "\\\"");
                result += indent + string.Format("    \"{0}\",\r\n", n);
            }
            return result.Substring(0, result.Length - 3) + "),\r\n";
        }

        public static string AuditLogEntryToCode(AuditLogEntry entry)
        {
            var sb = new StringBuilder();

            sb.Append("            new LogEntryMessages(\r\n");
            sb.Append(LogMessageToCode(entry.UndoRedo, 4));
            sb.Append(LogMessageToCode(entry.Summary, 4));

            sb.Append("                new[]\r\n                {\r\n");
            sb.Append(string.Join(string.Empty, entry.AllInfo.Select(info => LogMessageToCode(info, 5))));

            sb.Append("                }");

            if (!string.IsNullOrEmpty(entry.ExtraInfo))
                sb.Append(", @\"" + entry.ExtraInfo + "\"");
            sb.Append("),");

            return sb.ToString();
        }

        public static void WaitForAuditLogForm(AuditLogForm form)
        {
            AbstractFunctionalTest.WaitForConditionUI(() => form.BindingListSource.IsComplete);
        }
    }
}
