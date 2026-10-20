using System;
using System.Text.Json.Serialization;

namespace VideoNarrator.Models
{
    public class FewShotExample
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonPropertyName("topic")]
        public string Topic { get; set; } = string.Empty;

        [JsonPropertyName("positive_example")]
        public string PositiveExample { get; set; } = string.Empty;

        [JsonPropertyName("negative_example")]
        public string NegativeExample { get; set; } = string.Empty;

        [JsonPropertyName("explanation")]
        public string Explanation { get; set; } = string.Empty;

        [JsonPropertyName("persona_id")]
        public string? PersonaId { get; set; }

        public FewShotExample Clone()
        {
            return new FewShotExample
            {
                Id = Guid.NewGuid().ToString("N"),
                Topic = this.Topic,
                PositiveExample = this.PositiveExample,
                NegativeExample = this.NegativeExample,
                Explanation = this.Explanation,
                PersonaId = this.PersonaId
            };
        }
    }
}
