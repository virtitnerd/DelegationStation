namespace DelegationStationShared.Models
{
    public class TagConsolidationParameters
    {
        public List<TagConsolidationPair> Pairs { get; set; }

        public TagConsolidationParameters()
        {
            Pairs = new List<TagConsolidationPair>();
        }
    }

    public class TagConsolidationPair
    {
        public string OldTagId { get; set; }
        public string NewTagId { get; set; }

        public TagConsolidationPair()
        {
            OldTagId = string.Empty;
            NewTagId = string.Empty;
        }
    }
}
