using System.ComponentModel.DataAnnotations;

namespace MVCVideoGuide.Data.Entities
{
    public class Product
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Give the wallpaper a title.")]
        [StringLength(100)]
        public string Item { get; set; } = "";

        // 0.0 og ikke 0: med heltal konverterer Range prisen til int og crasher ved NaN eller meget store tal.
        [Range(0.0, 100000.0, ErrorMessage = "Cost must be between 0 and 100000 DKK.")]
        public double Cost { get; set; }

        [Required(ErrorMessage = "Paste a link to the image.")]
        [Url(ErrorMessage = "That doesn't look like a valid URL.")] // Tillader kun http, https og ftp, så "javascript:" afvises.
        [StringLength(2048)]
        public string Img { get; set; } = "";

        [Required(ErrorMessage = "Add a tag, e.g. nature.")]
        [StringLength(30)]
        // Mønstret skal virke både i .NET og i browserens JavaScript-validering, derfor ingen \p{L}.
        [RegularExpression(@"^#?\s*[A-Za-z0-9æøåÆØÅ_-]+\s*$",ErrorMessage = "Use one word for the tag (letters, numbers, - or _).")]
        public string Tag { get; set; } = "";
    }
}
