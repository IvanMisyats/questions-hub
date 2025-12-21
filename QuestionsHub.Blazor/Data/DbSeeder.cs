using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(QuestionsHubDbContext context)
    {
        // Check if we need to seed questions for existing packages (handles case where packages were added without questions)
        var existingPackages = await context.Packages
            .Include(p => p.Tours)
            .ThenInclude(t => t.Questions)
            .ToListAsync();

        if (existingPackages.Any())
        {
            // Check if any packages have tours without questions
            var needsQuestionSeeding = existingPackages
                .SelectMany(p => p.Tours)
                .Any(t => !t.Questions.Any());

            if (needsQuestionSeeding)
            {
                await SeedQuestionsForExistingToursAsync(context, existingPackages);
            }
            return;
        }

        // Full seed - no packages exist
        var packages = CreatePackages();

        context.Packages.AddRange(packages);
        await context.SaveChangesAsync();
    }

    private static async Task SeedQuestionsForExistingToursAsync(QuestionsHubDbContext context, List<Package> packages)
    {
        foreach (var package in packages)
        {
            foreach (var tour in package.Tours.Where(t => !t.Questions.Any()))
            {
                var tourNumber = int.TryParse(tour.Number, out var num) ? num : 1;
                var questions = GenerateQuestionsForTour(tourNumber, tour.Editors.FirstOrDefault() ?? "Редактор");
                tour.Questions.AddRange(questions);
            }

            // Update TotalQuestions count for the package
            package.TotalQuestions = package.Tours.Sum(t => t.Questions.Count);
        }

        await context.SaveChangesAsync();
    }

    private static List<Package> CreatePackages()
    {
        return
        [
            new Package
            {
                Title = "Кубок Весни 2025",
                Editors = ["Іван Петренко", "Марія Коваленко", "Олексій Шевченко"],
                PlayedAt = new DateOnly(2025, 3, 15),
                Description = "Весняний синхронний турнір для команд різного рівня підготовки.",
                Tours = CreateToursForPackage(["Іван Петренко", "Марія Коваленко", "Олексій Шевченко"]),
                TotalQuestions = 36
            },
            new Package
            {
                Title = "Чемпіонат України 2024",
                Editors = ["Андрій Мельник", "Катерина Бондаренко"],
                PlayedAt = new DateOnly(2024, 11, 20),
                Description = "Офіційний чемпіонат України серед команд Що?Де?Коли?",
                Tours = CreateToursForPackage(["Андрій Мельник", "Катерина Бондаренко", "Сергій Литвиненко"]),
                TotalQuestions = 36
            },
            new Package
            {
                Title = "Осінній кубок Львова",
                Editors = ["Олена Ткаченко", "Василь Гончаренко", "Наталія Кравчук"],
                PlayedAt = new DateOnly(2024, 10, 5),
                Description = "Традиційний осінній турнір у Львові для команд західного регіону.",
                Tours = CreateToursForPackage(["Олена Ткаченко", "Василь Гончаренко", "Наталія Кравчук"]),
                TotalQuestions = 36
            }
        ];
    }

    private static List<Tour> CreateToursForPackage(string[] editors)
    {
        return
        [
            new Tour
            {
                Number = "1",
                Title = "Тур перший",
                Editors = [editors[0]],
                Questions = GenerateQuestionsForTour(1, editors[0])
            },
            new Tour
            {
                Number = "2",
                Title = "Тур другий",
                Editors = [editors.Length > 1 ? editors[1] : editors[0]],
                Questions = GenerateQuestionsForTour(2, editors.Length > 1 ? editors[1] : editors[0])
            },
            new Tour
            {
                Number = "3",
                Title = "Тур третій",
                Editors = [editors.Length > 2 ? editors[2] : editors[0]],
                Questions = GenerateQuestionsForTour(3, editors.Length > 2 ? editors[2] : editors[0])
            }
        ];
    }

    private static List<Question> GenerateQuestionsForTour(int tourNumber, string editor)
    {
        var questions = new List<Question>();
        var questionTemplates = GetQuestionTemplates();

        for (int i = 1; i <= 12; i++)
        {
            var globalIndex = ((tourNumber - 1) * 12 + i - 1) % questionTemplates.Count;
            var template = questionTemplates[globalIndex];

            questions.Add(new Question
            {
                Number = i.ToString(),
                Text = template.Text,
                Answer = template.Answer,
                AcceptedAnswers = template.AcceptedAnswers,
                Comment = template.Comment,
                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                Authors = [editor]
            });
        }

        return questions;
    }

    private static List<QuestionTemplate> GetQuestionTemplates()
    {
        return
        [
            new("Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore. Назвіть ІКС.",
                "Dolor", null, "Lorem ipsum - класичний текст-заповнювач."),

            new("Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris. Що ми замінили на ІКСИ?",
                "Veniam", "вені; веніам", "Продовження класичного тексту."),

            new("Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore. Назвіть це двома словами.",
                "Irure dolor", null, "Ще один фрагмент Lorem ipsum."),

            new("Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt. Хто саме?",
                "Occaecat", null, "Фінальна частина першого абзацу Lorem ipsum."),

            new("Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium. Про що йдеться?",
                "Perspiciatis", null, "Початок другого абзацу тексту."),

            new("Nemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit aut fugit. Назвіть NEMO.",
                "Ніхто", "нема; немає", "Nemo латиною означає 'ніхто'."),

            new("Neque porro quisquam est, qui dolorem ipsum quia dolor sit amet. Що це за текст?",
                "Lorem ipsum", null, "Найвідоміший текст-заповнювач у світі."),

            new("Consectetur, adipisci velit, sed quia non numquam eius modi tempora incidunt. Яке слово пропущено?",
                "Adipisci", null, "Текст походить від Цицерона."),

            new("Ut labore et dolore magnam aliquam quaerat voluptatem. Хто це написав?",
                "Цицерон", "Марк Тулій Цицерон", "Оригінальний текст з трактату 'Про межі добра і зла'."),

            new("Quis autem vel eum iure reprehenderit qui in ea voluptate velit esse. Назвіть ЦЕ.",
                "Voluptate", null, "Латинське слово, що означає 'задоволення'."),

            new("At vero eos et accusamus et iusto odio dignissimos ducimus. Про кого йдеться?",
                "Ducimus", null, "Означає 'ми ведемо' латиною."),

            new("Qui blanditiis praesentium voluptatum deleniti atque corrupti. Що означає blanditiis?",
                "Лестощі", "підлесливість; лестивість", "Латинське слово з кореневим значенням 'лестити'."),

            new("Quos dolores et quas molestias excepturi sint occaecati. Назвіть ІКС.",
                "Molestias", null, "Означає 'неприємності' латиною."),

            new("Cupiditate non provident, similique sunt in culpa qui officia deserunt mollitia. Що таке mollitia?",
                "М'якість", "ніжність; слабкість", "Латинське слово mollitia означає 'm'якість' або 'ніжність'."),

            new("Animi, id est laborum et dolorum fuga. Як перекладається fuga?",
                "Втеча", "уникнення; біг", "Латинське fuga означає 'втеча'."),

            new("Et harum quidem rerum facilis est et expedita distinctio. Що означає expedita?",
                "Швидкий", "вільний; готовий", "Латинське expedita означає 'вільний від перешкод'."),

            new("Nam libero tempore, cum soluta nobis est eligendi optio. Що обирають?",
                "Optio", null, "Латинське слово для 'вибір' або 'можливість'."),

            new("Temporibus autem quibusdam et aut officiis debitis aut rerum necessitatibus saepe eveniet. Що таке necessitatibus?",
                "Необхідність", "потреба", "Латинське слово, що означає 'необхідності'."),

            new("Itaque earum rerum hic tenetur a sapiente delectus. Хто такий sapiente?",
                "Мудрець", "мудра людина", "Sapiente латиною означає 'мудрий'."),

            new("Ut aut reiciendis voluptatibus maiores alias consequatur aut perferendis doloribus. Що означає reiciendis?",
                "Відкидання", "відмова", "Латинське дієслово, що означає 'відкидати'."),

            new("Asperiores repellat omnis voluptas assumenda est, omnis dolor repellendus. Назвіть repellendus.",
                "Відштовхування", "відторгнення", "Латинське слово, що означає 'те, що має бути відштовхнуте'."),

            new("Similique sunt in culpa qui officia deserunt mollitia animi. Що означає culpa?",
                "Вина", "провина", "Латинське слово culpa означає 'вина' або 'провина'."),

            new("Laborum et dolorum fuga est rerum facilis expedita distinctio. Що таке distinctio?",
                "Розрізнення", "відмінність", "Латинське слово, що означає 'розрізнення' або 'відмінність'."),

            new("Nam libero tempore soluta nobis eligendi optio cumque nihil. Як перекладається nihil?",
                "Ніщо", "нічого", "Латинське nihil означає 'ніщо'."),

            new("Impedit quo minus id quod maxime placeat facere possimus. Що означає placeat?",
                "Подобається", "приємно", "Латинське дієслово placere означає 'подобатися'."),

            new("Omnis voluptas assumenda est laboriosam nisi ut aliquid. Назвіть laboriosam.",
                "Працьовитий", "трудомісткий", "Латинське слово, що означає 'працьовитий' або 'важкий'."),

            new("Ex ea commodi consequatur quis autem vel eum iure reprehenderit. Що таке commodi?",
                "Вигода", "зручність; користь", "Латинське слово commodi означає 'вигода' або 'зручність'."),

            new("Voluptatem sequi nesciunt neque porro quisquam est qui dolorem. Хто такий quisquam?",
                "Хтось", "будь-хто", "Латинське займенник, що означає 'хтось' або 'будь-хто'."),

            new("Fugiat quo voluptas nulla pariatur excepteur sint occaecat cupidatat. Що означає pariatur?",
                "Народжується", "виникає", "Латинське дієслово parere означає 'народжувати' або 'виникати'."),

            new("Accusantium doloremque laudantium totam rem aperiam eaque ipsa. Назвіть laudantium.",
                "Похвала", "схвалення", "Латинське слово laudare означає 'хвалити'."),

            new("Inventore veritatis et quasi architecto beatae vitae dicta sunt. Що таке veritatis?",
                "Істина", "правда", "Латинське слово veritas означає 'істина' або 'правда'."),

            new("Explicabo nemo enim ipsam voluptatem quia voluptas sit aspernatur. Як перекладається aspernatur?",
                "Зневажає", "відкидає", "Латинське дієслово aspernari означає 'зневажати'."),

            new("Ratione voluptatem sequi nesciunt neque porro quisquam dolorem. Що означає ratione?",
                "Розум", "причина; міркування", "Латинське слово ratio означає 'розум' або 'причина'."),

            new("Magni dolores eos qui ratione voluptatem sequi nesciunt. Назвіть magni.",
                "Великий", "значний", "Латинське слово magnus означає 'великий'."),

            new("Dolorem ipsum quia dolor sit amet consectetur adipisci velit. Що таке adipisci?",
                "Набувати", "отримувати", "Латинське дієслово adipisci означає 'набувати' або 'досягати'."),

            new("Quia consequuntur magni dolores eos qui ratione voluptatem. Назвіть consequuntur.",
                "Наслідок", "випливає", "Латинське дієслово consequi означає 'слідувати за' або 'випливати'.")
        ];
    }

    private record QuestionTemplate(string Text, string Answer, string? AcceptedAnswers, string Comment);
}
