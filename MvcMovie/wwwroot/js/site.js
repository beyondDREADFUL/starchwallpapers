// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Login() er fjernet: login tjekkes nu på serveren (HomeController.Login).

function FilterTags() { // Viser kun de wallpapers hvis tag matcher. Tomt felt = vis alle.
    // Tagget ligger i data-tag i stedet for som CSS-klasse, så store/små bogstaver og et "#" foran ikke betyder noget.
    var tag = document.getElementById("FilterTag").value.trim().replace(/^#+/, "").trim().toLowerCase();
    var products = document.querySelectorAll("#productcontainer .product");
    var shown = 0;
    for (var i = 0; i < products.length; i++) {
        var match = tag === "" || products[i].dataset.tag === tag;
        products[i].style.display = match ? "" : "none";
        if (match) shown++;
    }
    document.getElementById("nomatch").style.display = shown === 0 && products.length > 0 ? "block" : "none";
}

function ResetFilter() { // Resetter filtre og gør alt synligt igen. Rydder også tag-feltet.
    document.getElementById("FilterTag").value = "";
    FilterTags();
}
