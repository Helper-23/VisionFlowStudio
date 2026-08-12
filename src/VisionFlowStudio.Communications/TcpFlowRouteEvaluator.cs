using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.Communications
{
    public sealed class TcpFlowRouteEvaluation
    {
        public StationRecipeFlowDefinition Flow { get; set; }
        public IDictionary<string, object> TriggerData { get; set; }
        public string MatchValue { get; set; } = string.Empty;
        public bool Matched { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    public static class TcpFlowRouteEvaluator
    {
        public static IReadOnlyList<TcpFlowRouteEvaluation> Evaluate(
            IEnumerable<StationRecipeFlowDefinition> flows,
            CommunicationDefinition channel,
            string message,
            string connectionId)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            var results = new List<TcpFlowRouteEvaluation>();
            foreach (var flow in (flows ?? Enumerable.Empty<StationRecipeFlowDefinition>()).Where(x => x != null && x.Enabled))
            {
                var trigger = flow.Flow == null ? null : flow.Flow.CommunicationTrigger;
                if (trigger == null || !string.Equals(trigger.Channel, channel.Name, StringComparison.OrdinalIgnoreCase)) continue;
                var evaluation = new TcpFlowRouteEvaluation { Flow = flow };
                try
                {
                    if (string.IsNullOrEmpty(trigger.ExpectedValue))
                        throw new InvalidOperationException("TCP/IP 通信触发的指定字符串不能为空");

                    var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "CommunicationTrigger.Raw", message ?? string.Empty },
                        { "CommunicationTrigger.ConnectionId", connectionId ?? string.Empty }
                    };
                    var fields = CommunicationRegistry.ExtractTextFields(
                        message,
                        string.IsNullOrEmpty(channel.FieldSeparator) ? "|" : channel.FieldSeparator,
                        trigger.Fields ?? new List<CommunicationFieldExtractionDefinition>());
                    foreach (var pair in fields) data["CommunicationTrigger." + pair.Key] = pair.Value;

                    var key = string.IsNullOrWhiteSpace(trigger.MatchField)
                        ? "CommunicationTrigger.Raw"
                        : "CommunicationTrigger." + trigger.MatchField.Trim();
                    object value;
                    if (!data.TryGetValue(key, out value)) throw new InvalidOperationException("触发匹配字段不存在：" + key);
                    evaluation.TriggerData = data;
                    evaluation.MatchValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    evaluation.Matched = string.Equals(trigger.Mode, "TextContains", StringComparison.OrdinalIgnoreCase)
                        ? evaluation.MatchValue.IndexOf(trigger.ExpectedValue, StringComparison.Ordinal) >= 0
                        : string.Equals(evaluation.MatchValue, trigger.ExpectedValue, StringComparison.Ordinal);
                }
                catch (Exception ex)
                {
                    evaluation.Error = ex.Message;
                }
                results.Add(evaluation);
            }
            return results;
        }
    }
}
