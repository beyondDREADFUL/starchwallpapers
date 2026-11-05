namespace MVCVideoGuide.Data.Entities
{
    public class NASA
    {
        public int Id { get; set; }
        public string? Copyright { get; set; } // Mange APOD-billeder er public domain og har ingen copyright.
        public string Date { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public string Hdimg { get; set; } = "";
        public string Title { get; set; } = "";
        public string Thumbnail { get; set; } = "";
        public bool IsVideo { get; set; } // På video-dage er Thumbnail et stillbillede og Hdimg et link til videoen.
    }
}
