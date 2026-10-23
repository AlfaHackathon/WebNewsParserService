using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

class Program
{
    static void Main()
    {
        var options = new ChromeOptions();

        // === 1. Подмена User-Agent ===
        // Укажите любой UA, например свежий Chrome на Windows:
        const string userAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/126.0.0.0 Safari/537.36";

        options.AddArgument($"--user-agent={userAgent}");
        
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);

        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddArgument("--start-maximized");
        options.AddArgument("--no-default-browser-check");
        options.AddArgument("--disable-infobars");

        using (IWebDriver driver = new ChromeDriver(options))
        {
            if (driver is ChromeDriver chrome)
            {
                
                chrome.ExecuteCdpCommand(
                    "Page.addScriptToEvaluateOnNewDocument",
                    new Dictionary<string, object>
                    {
                        ["source"] = "Object.defineProperty(navigator, 'webdriver', { get: () => undefined });"
                    });
            }

            driver.Navigate().GoToUrl("https://tass.ru/politika/28243441");
            Thread.Sleep(3000);
            var newsArticle = driver.FindElement(By.CssSelector(".ContentPageContainer_content__45nmT"));
            var newsHeadingsAndParagraph = newsArticle.FindElements(By.CssSelector(".Paragraph_paragraph__2FiCb, .Heading_block__zRskh"));
            Console.WriteLine(string.Join(Environment.NewLine, newsHeadingsAndParagraph.ToList().Select(e => e.Text)));
            Console.ReadLine();
        }
    }
}