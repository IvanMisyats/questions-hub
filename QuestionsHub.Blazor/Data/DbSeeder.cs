using Microsoft.EntityFrameworkCore;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(QuestionsHubDbContext context)
    {
        if (await context.Packages.AnyAsync())
        {
            return; // Database already seeded
        }

        var packages = new List<Package>
        {
            new Package
            {
                Title = "Кубок Весни 2025",
                Editors = ["Іван Петренко", "Марія Коваленко", "Олексій Шевченко"],
                PlayedAt = new DateOnly(2025, 3, 15),
                Description = "Весняний синхронний турнір для команд різного рівня підготовки.",
                Tours =
                [
                    new Tour
                    {
                        Number = "1",
                        Title = "Тур перший",
                        Editors = ["Іван Петренко"],
                        Questions =
                        [
                            new Question
                            {
                                Number = "1",
                                Text = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore. Назвіть ІКС.",
                                Answer = "Dolor",
                                Comment = "Lorem ipsum - класичний текст-заповнювач.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Іван Петренко"]
                            },
                            new Question
                            {
                                Number = "2",
                                Text = "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris. Що ми замінили на ІКСИ?",
                                Answer = "Veniam",
                                AcceptedAnswers = "вені; веніам",
                                Comment = "Продовження класичного тексту.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Іван Петренко"]
                            },
                            new Question
                            {
                                Number = "3",
                                Text = "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore. Назвіть це двома словами.",
                                Answer = "Irure dolor",
                                Comment = "Ще один фрагмент Lorem ipsum.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Марія Коваленко"]
                            }
                        ]
                    },
                    new Tour
                    {
                        Number = "2",
                        Title = "Тур другий",
                        Editors = ["Марія Коваленко"],
                        Questions =
                        [
                            new Question
                            {
                                Number = "4",
                                Text = "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt. Хто саме?",
                                Answer = "Occaecat",
                                Comment = "Фінальна частина першого абзацу Lorem ipsum.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Марія Коваленко"]
                            },
                            new Question
                            {
                                Number = "5",
                                Text = "Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium. Про що йдеться?",
                                Answer = "Perspiciatis",
                                Comment = "Початок другого абзацу тексту.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Марія Коваленко"]
                            },
                            new Question
                            {
                                Number = "6",
                                Text = "Nemo enim ipsam voluptatem quia voluptas sit aspernatur aut odit aut fugit. Назвіть NEMO.",
                                Answer = "Ніхто",
                                AcceptedAnswers = "нема; немає",
                                Comment = "Nemo латиною означає 'ніхто'.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Олексій Шевченко"]
                            }
                        ]
                    }
                ]
            },
            new Package
            {
                Title = "Чемпіонат України 2024",
                Editors = ["Андрій Мельник", "Катерина Бондаренко"],
                PlayedAt = new DateOnly(2024, 11, 20),
                Description = "Офіційний чемпіонат України серед команд Що?Де?Коли?",
                Tours =
                [
                    new Tour
                    {
                        Number = "1",
                        Title = "Перший тур",
                        Editors = ["Андрій Мельник"],
                        Questions =
                        [
                            new Question
                            {
                                Number = "1",
                                Text = "Neque porro quisquam est, qui dolorem ipsum quia dolor sit amet. Що це за текст?",
                                Answer = "Lorem ipsum",
                                Comment = "Найвідоміший текст-заповнювач у світі.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Андрій Мельник"]
                            },
                            new Question
                            {
                                Number = "2",
                                Text = "Consectetur, adipisci velit, sed quia non numquam eius modi tempora incidunt. Яке слово пропущено?",
                                Answer = "Adipisci",
                                Comment = "Текст походить від Цицерона.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Андрій Мельник"]
                            },
                            new Question
                            {
                                Number = "3",
                                Text = "Ut labore et dolore magnam aliquam quaerat voluptatem. Хто це написав?",
                                Answer = "Цицерон",
                                AcceptedAnswers = "Марк Тулій Цицерон",
                                Comment = "Оригінальний текст з трактату 'Про межі добра і зла'.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Катерина Бондаренко"]
                            }
                        ]
                    },
                    new Tour
                    {
                        Number = "2",
                        Title = "Другий тур",
                        Editors = ["Катерина Бондаренко"],
                        Questions =
                        [
                            new Question
                            {
                                Number = "4",
                                Text = "Quis autem vel eum iure reprehenderit qui in ea voluptate velit esse. Назвіть ЦЕ.",
                                Answer = "Voluptate",
                                Comment = "Латинське слово, що означає 'задоволення'.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Катерина Бондаренко"]
                            },
                            new Question
                            {
                                Number = "5",
                                Text = "At vero eos et accusamus et iusto odio dignissimos ducimus. Про кого йдеться?",
                                Answer = "Ducimus",
                                Comment = "Означає 'ми ведемо' латиною.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Катерина Бондаренко"]
                            }
                        ]
                    }
                ]
            },
            new Package
            {
                Title = "Осінній кубок Львова",
                Editors = ["Олена Ткаченко", "Василь Гончаренко", "Наталія Кравчук"],
                PlayedAt = new DateOnly(2024, 10, 5),
                Description = "Традиційний осінній турнір у Львові для команд західного регіону.",
                Tours =
                [
                    new Tour
                    {
                        Number = "1",
                        Title = "Тур 1",
                        Editors = ["Олена Ткаченко"],
                        Questions =
                        [
                            new Question
                            {
                                Number = "1",
                                Text = "Qui blanditiis praesentium voluptatum deleniti atque corrupti. Що означає blanditiis?",
                                Answer = "Лестощі",
                                AcceptedAnswers = "підлесливість; лестивість",
                                Comment = "Латинське слово з кореневим значенням 'лестити'.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Олена Ткаченко"]
                            },
                            new Question
                            {
                                Number = "2",
                                Text = "Quos dolores et quas molestias excepturi sint occaecati. Назвіть ІКС.",
                                Answer = "Molestias",
                                Comment = "Означає 'неприємності' латиною.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Олена Ткаченко"]
                            },
                            new Question
                            {
                                Number = "3",
                                Text = "Cupiditate non provident, similique sunt in culpa qui officia deserunt mollitia. Що таке mollitia?",
                                Answer = "М'якість",
                                AcceptedAnswers = "ніжність; слабкість",
                                Comment = "Латинське слово mollitia означає 'm'якість' або 'ніжність'.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Василь Гончаренко"]
                            }
                        ]
                    },
                    new Tour
                    {
                        Number = "2",
                        Title = "Тур 2",
                        Editors = ["Василь Гончаренко"],
                        Questions =
                        [
                            new Question
                            {
                                Number = "4",
                                Text = "Animi, id est laborum et dolorum fuga. Як перекладається fuga?",
                                Answer = "Втеча",
                                AcceptedAnswers = "уникнення; біг",
                                Comment = "Латинське fuga означає 'втеча'.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Василь Гончаренко"]
                            },
                            new Question
                            {
                                Number = "5",
                                Text = "Et harum quidem rerum facilis est et expedita distinctio. Що означає expedita?",
                                Answer = "Швидкий",
                                AcceptedAnswers = "вільний; готовий",
                                Comment = "Латинське expedita означає 'вільний від перешкод'.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Наталія Кравчук"]
                            },
                            new Question
                            {
                                Number = "6",
                                Text = "Nam libero tempore, cum soluta nobis est eligendi optio. Що обирають?",
                                Answer = "Optio",
                                Comment = "Латинське слово для 'вибір' або 'можливість'.",
                                Source = "https://uk.wikipedia.org/wiki/Lorem_ipsum",
                                Authors = ["Наталія Кравчук"]
                            }
                        ]
                    }
                ]
            }
        };

        context.Packages.AddRange(packages);
        await context.SaveChangesAsync();
    }
}

