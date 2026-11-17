namespace Ipl.Infrastructure.Persistence;

/// <summary>
/// The ten real franchises and their IPL 2026 squads as published after the 2026 auction, used as reference data.
/// Only factual squad information is recorded here: names, team, role group, country, overseas/capped status, keeper,
/// batting hand and bowling style where publicly known ("?" = not recorded). No statistics, prices (except those the
/// sources state), dates of birth, photos or logos are included; those must come from licensed sources.
/// </summary>
public static class RealSquads
{
    public const string Source =
        "IPL 2026 squad lists: Outlook India \"IPL 2026: Full Squads Of All The 10 Franchises\", cross-checked with Wisden " +
        "\"IPL 2026 squads: complete team lists after the mini auction\" (accessed 17 Nov 2026). Roles/styles from public profiles where known.";

    public sealed record Team(string Code, string Name, string ShortName, string City, string Ground, string Owner, int Founded, string Primary, string Secondary);

    public static readonly Team[] Teams =
    [
        new("CSK", "Chennai Super Kings", "Super Kings", "Chennai", "M. A. Chidambaram Stadium", "Chennai Super Kings Cricket Ltd", 2008, "#F9CD05", "#0B4DA2"),
        new("MI", "Mumbai Indians", "Indians", "Mumbai", "Wankhede Stadium", "Indiawin Sports (Reliance Industries)", 2008, "#004BA0", "#D1AB3E"),
        new("RCB", "Royal Challengers Bengaluru", "Royal Challengers", "Bengaluru", "M. Chinnaswamy Stadium", "Royal Challengers Sports Pvt Ltd", 2008, "#EC1C24", "#2B2A29"),
        new("KKR", "Kolkata Knight Riders", "Knight Riders", "Kolkata", "Eden Gardens", "Knight Riders Group", 2008, "#3A225D", "#B3A123"),
        new("DC", "Delhi Capitals", "Capitals", "Delhi", "Arun Jaitley Stadium", "GMR Group and JSW Group", 2008, "#17479E", "#EF1B23"),
        new("RR", "Rajasthan Royals", "Royals", "Jaipur", "Sawai Mansingh Stadium", "Royal Sports Group", 2008, "#EA1A85", "#254AA5"),
        new("PBKS", "Punjab Kings", "Kings", "Mullanpur", "Maharaja Yadavindra Singh International Cricket Stadium", "KPH Dream Cricket Pvt Ltd", 2008, "#ED1B24", "#A7A9AC"),
        new("SRH", "Sunrisers Hyderabad", "Sunrisers", "Hyderabad", "Rajiv Gandhi International Stadium", "Sun TV Network", 2013, "#FF822A", "#1B1B1B"),
        new("LSG", "Lucknow Super Giants", "Super Giants", "Lucknow", "BRSABV Ekana Cricket Stadium", "RPSG Group", 2022, "#0057E2", "#F28B00"),
        new("GT", "Gujarat Titans", "Titans", "Ahmedabad", "Narendra Modi Stadium", "Torrent Group and CVC Capital Partners", 2022, "#1B2133", "#B09A57"),
    ];

    /// <summary>IPL finals 2008–2025: year, champion code, runner-up code (null = franchise no longer exists).</summary>
    public static readonly (int Year, string? Champion, string? RunnerUp)[] Finals =
    [
        (2008, "RR", "CSK"), (2009, null, "RCB"), (2010, "CSK", "MI"), (2011, "CSK", "RCB"), (2012, "KKR", "CSK"),
        (2013, "MI", "CSK"), (2014, "KKR", "PBKS"), (2015, "MI", "CSK"), (2016, "SRH", "RCB"), (2017, "MI", null),
        (2018, "CSK", "SRH"), (2019, "MI", "CSK"), (2020, "MI", "DC"), (2021, "CSK", "KKR"), (2022, "GT", "RR"),
        (2023, "CSK", "GT"), (2024, "KKR", "SRH"), (2025, "RCB", "PBKS"),
    ];

    /// <summary>Prices stated by the sources (₹). Everyone else: price not recorded.</summary>
    public static readonly Dictionary<string, decimal> KnownPrices = new()
    {
        ["Cameron Green"] = 25.20m * 10_000_000, ["Prashant Veer"] = 14.20m * 10_000_000, ["Kartik Sharma"] = 14.20m * 10_000_000,
        ["Venkatesh Iyer"] = 7m * 10_000_000, ["Prithvi Shaw"] = 75m * 100_000,
    };

    /// <summary>Players marked as signed at the December 2025 auction by the sources.</summary>
    public static readonly HashSet<string> SignedAtAuction =
    [
        "Akeal Hosein", "Matt Henry", "Matthew Short", "Sarfaraz Khan", "Zak Foulkes", "Aman Khan", "Rahul Chahar", "Prashant Veer", "Kartik Sharma",
        "Auqib Nabi", "David Miller", "Kyle Jamieson", "Pathum Nissanka", "Sahil Parakh", "Prithvi Shaw", "Tom Banton", "Jason Holder", "Ashok Sharma",
        "Prithvi Raj Yarra", "Luke Wood", "Cameron Green", "Sarthak Ranjan", "Finn Allen", "Kartik Tyagi", "Daksh Kamra", "Prashant Solanki",
        "Rachin Ravindra", "Rahul Tripathi", "Tim Seifert", "Mukul Choudhary", "Akshat Raghuwanshi", "Anrich Nortje", "Naman Tiwari", "Quinton de Kock",
        "Atharva Ankolekar", "Danish Malewar", "Trent Boult", "Mayank Rawat", "Mohammad Izhar", "Ben Dwarshuis", "Cooper Connolly", "Pravin Dubey",
        "Vishal Nishad", "Ravi Singh", "Ravi Bishnoi", "Aman Rao", "Sushant Mishra", "Yash Raj Punja", "Vignesh Puthur", "Brijesh Sharma", "Adam Milne",
        "Kuldeep Sen", "Venkatesh Iyer", "Satvik Deswal", "Jordan Cox", "Mangesh Yadav", "Vihaan Malhotra", "Jacob Duffy", "Kanishk Chouhan",
        "Vicky Ostwal", "Shivang Kumar", "Salil Arora", "Krains Fuletra", "Sakib Hussain", "Liam Livingstone", "Onkar Tarmale", "Shivam Mavi",
        "Amit Kumar", "Jack Edwards", "Praful Hinge",
    ];

    /// <summary>Team captains as listed by the source ("(C)").</summary>
    public static readonly Dictionary<string, string> Captains = new()
    {
        ["CSK"] = "Ruturaj Gaikwad", ["MI"] = "Hardik Pandya", ["RCB"] = "Rajat Patidar", ["KKR"] = "Ajinkya Rahane", ["DC"] = "Axar Patel",
        ["RR"] = "Riyan Parag", ["PBKS"] = "Shreyas Iyer", ["SRH"] = "Ishan Kishan", ["LSG"] = "Rishabh Pant", ["GT"] = "Shubman Gill",
    };

    // Team | Name | role (bat, wk, ar, bowl) | country | capped (1/0) | batting hand (R, L, ?) | bowling style ("" = does not bowl, "?" = not recorded)
    public const string Players = """
CSK|Ruturaj Gaikwad|bat|IND|1|R|
CSK|Dewald Brevis|bat|RSA|1|R|Right-arm leg-break
CSK|MS Dhoni|wk|IND|1|R|
CSK|Kartik Sharma|wk|IND|0|?|
CSK|Sarfaraz Khan|bat|IND|1|R|
CSK|Ayush Mhatre|bat|IND|0|R|?
CSK|Urvil Patel|wk|IND|0|R|
CSK|Sanju Samson|wk|IND|1|R|
CSK|Aman Khan|ar|IND|0|?|?
CSK|Shivam Dube|ar|IND|1|L|Right-arm medium
CSK|Zak Foulkes|ar|NZL|1|R|Right-arm fast-medium
CSK|Ramakrishna Ghosh|ar|IND|0|?|?
CSK|Anshul Kamboj|ar|IND|0|R|Right-arm medium-fast
CSK|Jamie Overton|ar|ENG|1|R|Right-arm fast
CSK|Matthew Short|ar|AUS|1|R|Right-arm off-break
CSK|Prashant Veer|ar|IND|0|L|Slow left-arm orthodox
CSK|Khaleel Ahmed|bowl|IND|1|R|Left-arm fast
CSK|Rahul Chahar|bowl|IND|1|R|Right-arm leg-break
CSK|Shreyas Gopal|bowl|IND|0|R|Right-arm leg-break
CSK|Gurjapneet Singh|bowl|IND|0|?|Left-arm fast-medium
CSK|Matt Henry|bowl|NZL|1|R|Right-arm fast
CSK|Akeal Hosein|bowl|WIN|1|L|Slow left-arm orthodox
CSK|Spencer Johnson|bowl|AUS|1|L|Left-arm fast
CSK|Mukesh Choudhary|bowl|IND|0|?|Left-arm medium-fast
CSK|Noor Ahmad|bowl|AFG|1|?|Left-arm wrist-spin
MI|Hardik Pandya|ar|IND|1|R|Right-arm fast-medium
MI|Quinton de Kock|wk|RSA|1|L|
MI|Danish Malewar|bat|IND|0|?|
MI|Robin Minz|wk|IND|0|L|
MI|Ryan Rickelton|wk|RSA|1|L|
MI|Sherfane Rutherford|bat|WIN|1|L|Right-arm medium-fast
MI|Rohit Sharma|bat|IND|1|R|Right-arm off-break
MI|Suryakumar Yadav|bat|IND|1|R|
MI|Tilak Varma|bat|IND|1|L|Right-arm off-break
MI|Atharva Ankolekar|ar|IND|0|L|Slow left-arm orthodox
MI|Raj Angad Bawa|ar|IND|0|L|Right-arm medium-fast
MI|Corbin Bosch|ar|RSA|1|R|Right-arm fast-medium
MI|Will Jacks|ar|ENG|1|R|Right-arm off-break
MI|Mayank Rawat|ar|IND|0|?|?
MI|Naman Dhir|ar|IND|0|R|Right-arm off-break
MI|Mitchell Santner|ar|NZL|1|L|Slow left-arm orthodox
MI|Shardul Thakur|ar|IND|1|R|Right-arm medium-fast
MI|Ashwani Kumar|bowl|IND|0|?|Left-arm medium-fast
MI|Trent Boult|bowl|NZL|1|R|Left-arm fast-medium
MI|Jasprit Bumrah|bowl|IND|1|R|Right-arm fast
MI|Deepak Chahar|bowl|IND|1|R|Right-arm medium-fast
MI|Allah Ghazanfar|bowl|AFG|1|R|Right-arm off-break
MI|Mayank Markande|bowl|IND|1|R|Right-arm leg-break
MI|Mohammad Izhar|bowl|IND|0|?|?
MI|Raghu Sharma|bowl|IND|0|?|Right-arm leg-break
RCB|Rajat Patidar|bat|IND|1|R|
RCB|Jordan Cox|wk|ENG|1|R|
RCB|Tim David|bat|AUS|1|R|Right-arm off-break
RCB|Virat Kohli|bat|IND|1|R|
RCB|Devdutt Padikkal|bat|IND|1|L|
RCB|Phil Salt|wk|ENG|1|R|
RCB|Jitesh Sharma|wk|IND|1|R|
RCB|Jacob Bethell|ar|ENG|1|L|Slow left-arm orthodox
RCB|Kanishk Chouhan|ar|IND|0|?|?
RCB|Venkatesh Iyer|ar|IND|1|L|Right-arm medium-fast
RCB|Vihaan Malhotra|ar|IND|0|?|?
RCB|Mangesh Yadav|ar|IND|0|?|?
RCB|Krunal Pandya|ar|IND|1|L|Slow left-arm orthodox
RCB|Romario Shepherd|ar|WIN|1|R|Right-arm fast-medium
RCB|Abhinandan Singh|bowl|IND|0|?|Right-arm medium-fast
RCB|Satvik Deswal|bowl|IND|0|?|?
RCB|Jacob Duffy|bowl|NZL|1|R|Right-arm fast-medium
RCB|Bhuvneshwar Kumar|bowl|IND|1|R|Right-arm medium-fast
RCB|Vicky Ostwal|bowl|IND|0|?|Slow left-arm orthodox
RCB|Rasikh Salam|bowl|IND|0|R|Right-arm medium-fast
RCB|Suyash Sharma|bowl|IND|0|R|Right-arm leg-break
RCB|Swapnil Singh|bowl|IND|0|?|Slow left-arm orthodox
RCB|Nuwan Thushara|bowl|SRI|1|R|Right-arm fast-medium
KKR|Ajinkya Rahane|bat|IND|1|R|
KKR|Rinku Singh|bat|IND|1|L|
KKR|Finn Allen|bat|NZL|1|R|
KKR|Tejasvi Dahiya|bat|IND|0|?|
KKR|Manish Pandey|bat|IND|1|R|
KKR|Rovman Powell|bat|WIN|1|R|Right-arm medium
KKR|Angkrish Raghuvanshi|bat|IND|0|R|
KKR|Ramandeep Singh|bat|IND|1|R|Right-arm medium
KKR|Sarthak Ranjan|bat|IND|0|?|
KKR|Tim Seifert|wk|NZL|1|R|
KKR|Rahul Tripathi|bat|IND|1|R|
KKR|Daksh Kamra|ar|IND|0|?|?
KKR|Cameron Green|ar|AUS|1|R|Right-arm fast-medium
KKR|Sunil Narine|ar|WIN|1|L|Right-arm off-break
KKR|Rachin Ravindra|ar|NZL|1|L|Slow left-arm orthodox
KKR|Anukul Roy|ar|IND|0|L|Slow left-arm orthodox
KKR|Vaibhav Arora|bowl|IND|0|R|Right-arm medium-fast
KKR|Saurabh Dubey|bowl|IND|0|?|?
KKR|Kartik Tyagi|bowl|IND|0|R|Right-arm fast
KKR|Blessing Muzarabani|bowl|ZIM|1|R|Right-arm fast-medium
KKR|Prashant Solanki|bowl|IND|0|R|Right-arm leg-break
KKR|Umran Malik|bowl|IND|1|R|Right-arm fast
KKR|Varun Chakravarthy|bowl|IND|1|R|Right-arm leg-break
DC|Axar Patel|ar|IND|1|L|Slow left-arm orthodox
DC|Abishek Porel|wk|IND|0|L|
DC|David Miller|bat|RSA|1|L|
DC|Karun Nair|bat|IND|1|R|
DC|Pathum Nissanka|bat|SRI|1|R|
DC|Sahil Parakh|bat|IND|0|?|
DC|KL Rahul|wk|IND|1|R|
DC|Nitish Rana|bat|IND|1|L|Right-arm off-break
DC|Prithvi Shaw|bat|IND|1|R|
DC|Tristan Stubbs|wk|RSA|1|R|
DC|Ajay Mandal|ar|IND|0|L|Slow left-arm orthodox
DC|Sameer Rizvi|ar|IND|0|R|?
DC|Ashutosh Sharma|ar|IND|0|R|?
DC|Madhav Tiwari|ar|IND|0|?|?
DC|Auqib Nabi|bowl|IND|0|?|Right-arm medium-fast
DC|Dushmantha Chameera|bowl|SRI|1|R|Right-arm fast
DC|Kyle Jamieson|bowl|NZL|1|R|Right-arm fast-medium
DC|Kuldeep Yadav|bowl|IND|1|L|Left-arm wrist-spin
DC|Mukesh Kumar|bowl|IND|1|R|Right-arm medium-fast
DC|T Natarajan|bowl|IND|1|L|Left-arm medium-fast
DC|Lungi Ngidi|bowl|RSA|1|R|Right-arm fast
DC|Vipraj Nigam|bowl|IND|0|?|Right-arm leg-break
DC|Tripurana Vijay|bowl|IND|0|?|?
RR|Riyan Parag|ar|IND|1|R|Right-arm leg-break
RR|Aman Rao|bat|IND|0|?|
RR|Shubham Dubey|bat|IND|0|L|
RR|Shimron Hetmyer|bat|WIN|1|L|
RR|Yashasvi Jaiswal|bat|IND|1|L|Right-arm leg-break
RR|Dhruv Jurel|wk|IND|1|R|
RR|Lhuan-dre Pretorius|wk|RSA|1|L|
RR|Ravi Singh|wk|IND|0|?|
RR|Vaibhav Sooryavanshi|bat|IND|0|L|
RR|Donovan Ferreira|ar|RSA|1|R|Right-arm off-break
RR|Ravindra Jadeja|ar|IND|1|L|Slow left-arm orthodox
RR|Dasun Shanaka|ar|SRI|1|R|Right-arm medium
RR|Sam Curran|ar|ENG|1|L|Left-arm medium-fast
RR|Jofra Archer|bowl|ENG|1|R|Right-arm fast
RR|Brijesh Sharma|bowl|IND|0|?|?
RR|Nandre Burger|bowl|RSA|1|?|Left-arm fast
RR|Tushar Deshpande|bowl|IND|1|L|Right-arm medium-fast
RR|Kwena Maphaka|bowl|RSA|1|?|Left-arm fast
RR|Adam Milne|bowl|NZL|1|R|Right-arm fast
RR|Sushant Mishra|bowl|IND|0|?|Left-arm fast-medium
RR|Vignesh Puthur|bowl|IND|0|?|Left-arm wrist-spin
RR|Ravi Bishnoi|bowl|IND|1|R|Right-arm leg-break
RR|Sandeep Sharma|bowl|IND|1|R|Right-arm medium-fast
RR|Kuldeep Sen|bowl|IND|1|R|Right-arm fast
RR|Yash Raj Punja|bowl|IND|0|?|?
RR|Yudhvir Singh|bowl|IND|0|R|Right-arm medium-fast
PBKS|Shreyas Iyer|bat|IND|1|R|Right-arm leg-break
PBKS|Priyansh Arya|bat|IND|0|L|
PBKS|Pyla Avinash|bat|IND|0|?|
PBKS|Harnoor Singh|bat|IND|0|L|
PBKS|Prabhsimran Singh|wk|IND|0|R|
PBKS|Vishnu Vinod|wk|IND|0|R|
PBKS|Nehal Wadhera|bat|IND|0|L|Right-arm leg-break
PBKS|Azmatullah Omarzai|ar|AFG|1|R|Right-arm fast-medium
PBKS|Cooper Connolly|ar|AUS|1|L|Slow left-arm orthodox
PBKS|Marco Jansen|ar|RSA|1|L|Left-arm fast
PBKS|Musheer Khan|ar|IND|0|R|Slow left-arm orthodox
PBKS|Mitchell Owen|ar|AUS|1|R|Right-arm medium
PBKS|Shashank Singh|ar|IND|0|R|Right-arm off-break
PBKS|Marcus Stoinis|ar|AUS|1|R|Right-arm medium-fast
PBKS|Suryansh Shedge|ar|IND|0|R|Right-arm medium
PBKS|Arshdeep Singh|bowl|IND|1|L|Left-arm fast-medium
PBKS|Xavier Bartlett|bowl|AUS|1|R|Right-arm fast-medium
PBKS|Yuzvendra Chahal|bowl|IND|1|R|Right-arm leg-break
PBKS|Pravin Dubey|bowl|IND|0|?|Right-arm leg-break
PBKS|Ben Dwarshuis|bowl|AUS|1|L|Left-arm fast-medium
PBKS|Lockie Ferguson|bowl|NZL|1|R|Right-arm fast
PBKS|Harpreet Brar|bowl|IND|0|L|Slow left-arm orthodox
PBKS|Vishal Nishad|bowl|IND|0|?|?
PBKS|Vyshak Vijaykumar|bowl|IND|0|R|Right-arm medium-fast
PBKS|Yash Thakur|bowl|IND|0|R|Right-arm fast-medium
SRH|Ishan Kishan|wk|IND|1|L|
SRH|Salil Arora|wk|IND|0|?|
SRH|Travis Head|bat|AUS|1|L|Right-arm off-break
SRH|Heinrich Klaasen|wk|RSA|1|R|
SRH|Ravichandran Smaran|bat|IND|0|?|
SRH|Aniket Verma|bat|IND|0|R|
SRH|Abhishek Sharma|ar|IND|1|L|Slow left-arm orthodox
SRH|Brydon Carse|ar|ENG|1|R|Right-arm fast
SRH|Harsh Dubey|ar|IND|0|L|Slow left-arm orthodox
SRH|Krains Fuletra|ar|IND|0|?|?
SRH|Liam Livingstone|ar|ENG|1|R|Right-arm leg-break
SRH|Kamindu Mendis|ar|SRI|1|L|?
SRH|Nitish Kumar Reddy|ar|IND|1|R|Right-arm medium-fast
SRH|Jack Edwards|ar|AUS|0|R|Right-arm fast-medium
SRH|Harshal Patel|bowl|IND|1|R|Right-arm medium-fast
SRH|Shivam Mavi|bowl|IND|1|R|Right-arm fast-medium
SRH|Shivang Kumar|bowl|IND|0|?|?
SRH|Amit Kumar|bowl|IND|0|?|?
SRH|Praful Hinge|bowl|IND|0|?|?
SRH|Eshan Malinga|bowl|SRI|1|?|Right-arm fast-medium
SRH|David Payne|bowl|ENG|1|R|Left-arm fast-medium
SRH|Sakib Hussain|bowl|IND|0|?|Right-arm fast-medium
SRH|Onkar Tarmale|bowl|IND|0|?|?
SRH|Jaydev Unadkat|bowl|IND|1|R|Left-arm medium-fast
SRH|Zeeshan Ansari|bowl|IND|0|?|Right-arm leg-break
LSG|Rishabh Pant|wk|IND|1|L|
LSG|Abdul Samad|bat|IND|0|R|Right-arm leg-break
LSG|Akshat Raghuwanshi|bat|IND|0|?|
LSG|Ayush Badoni|bat|IND|0|R|Right-arm off-break
LSG|Matthew Breetzke|bat|RSA|1|R|
LSG|Mukul Choudhary|wk|IND|0|?|
LSG|Himmat Singh|bat|IND|0|R|
LSG|Aiden Markram|bat|RSA|1|R|Right-arm off-break
LSG|Nicholas Pooran|wk|WIN|1|L|
LSG|Arshin Kulkarni|ar|IND|0|R|Right-arm medium
LSG|Mitchell Marsh|ar|AUS|1|R|Right-arm medium-fast
LSG|Shahbaz Ahmed|ar|IND|1|L|Slow left-arm orthodox
LSG|Akash Singh|bowl|IND|0|?|Left-arm fast-medium
LSG|Avesh Khan|bowl|IND|1|R|Right-arm fast-medium
LSG|Mohammed Shami|bowl|IND|1|R|Right-arm fast
LSG|Mohsin Khan|bowl|IND|0|?|Left-arm fast-medium
LSG|Anrich Nortje|bowl|RSA|1|R|Right-arm fast
LSG|Prince Yadav|bowl|IND|0|?|Right-arm medium-fast
LSG|Digvesh Rathi|bowl|IND|0|?|Right-arm leg-break
LSG|Manimaran Siddharth|bowl|IND|0|?|Slow left-arm orthodox
LSG|Arjun Tendulkar|bowl|IND|0|L|Left-arm medium-fast
LSG|Naman Tiwari|bowl|IND|0|?|Left-arm fast-medium
LSG|Mayank Yadav|bowl|IND|1|R|Right-arm fast
GT|Shubman Gill|bat|IND|1|R|
GT|Anuj Rawat|wk|IND|0|L|
GT|Tom Banton|wk|ENG|1|R|
GT|Jos Buttler|wk|ENG|1|R|
GT|Kumar Kushagra|wk|IND|0|R|
GT|Sai Sudharsan|bat|IND|1|L|
GT|M Shahrukh Khan|bat|IND|0|R|Right-arm off-break
GT|Jason Holder|ar|WIN|1|R|Right-arm fast-medium
GT|Glenn Phillips|ar|NZL|1|R|Right-arm off-break
GT|Nishant Sindhu|ar|IND|0|L|Slow left-arm orthodox
GT|Manav Suthar|ar|IND|0|L|Slow left-arm orthodox
GT|Rahul Tewatia|ar|IND|0|L|Right-arm leg-break
GT|Washington Sundar|ar|IND|1|L|Right-arm off-break
GT|Rashid Khan|bowl|AFG|1|R|Right-arm leg-break
GT|Arshad Khan|bowl|IND|0|L|Left-arm medium-fast
GT|Ashok Sharma|bowl|IND|0|?|?
GT|Gurnoor Brar|bowl|IND|0|R|Right-arm fast-medium
GT|Mohammed Siraj|bowl|IND|1|R|Right-arm fast
GT|Prasidh Krishna|bowl|IND|1|R|Right-arm fast
GT|Prithvi Raj Yarra|bowl|IND|0|?|?
GT|Kagiso Rabada|bowl|RSA|1|L|Right-arm fast
GT|Sai Kishore|bowl|IND|1|L|Slow left-arm orthodox
GT|Ishant Sharma|bowl|IND|1|R|Right-arm fast
GT|Luke Wood|bowl|ENG|1|L|Left-arm fast-medium
GT|Jayant Yadav|bowl|IND|1|R|Right-arm off-break
""";
}
