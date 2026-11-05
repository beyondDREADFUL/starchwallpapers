namespace MVCVideoGuide.Data.Entities
{
    public class Bing
    {
        //Alle klasser i denne folder er class entities, dvs. klasser der matcher det indhold der ønskes som entity(tabel) i databasen.
        public int Id { get; set; }
        public string Date { get; set; } = "";
        public string Url { get; set; } = "";
        public string? Copyright { get; set; }
        public string Title { get; set; } = "";
    }
}
