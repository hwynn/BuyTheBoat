### What Is this?

BuyTheBoat is a budgeting program meant to answer a few basic questions I always have about my own budget:

1. **"How much free (unallocated) money do I have?"**
2. **"Do I have enough to buy X?"**
3. **"Am I on track to reach my finance goals I have set?"**
4. **"If I buy X anyways, how will I have to readjust my finance goals to compensate?"**

Basically it's a slightly simpler way to keep track of a budget than that huge spreadsheet we all make at some point.

 ### What privacy concerns should I have?

None. This program never connects to the internet. Unfortunately that means you'll have to enter in a lot of stuff by hand.
You'll probably have to go through your bank account and record a bunch of stuff to use in the program.
You will want to keep track of the current balance of all your accounts. 
And it'll help to write down all of your regular bills, how much they cost on average, what account they come out of, their frequency, and the exact date they tend to come out on.
It'll make entering it into this program much easier.

## Download/Installation instructions

The easy way: download the latest prebuilt version for your platform from the [Releases page](https://github.com/hwynn/BuyTheBoat/releases/latest).
Pick the file that matches your system:
- **Windows:** `BuyTheBoat-<version>-win-x64.zip`. Unzip it and double click `BuyTheBoat.exe`.
- **Linux:** `BuyTheBoat-<version>-linux-x64.tar.gz`. Extract it and run `BuyTheBoat`.

There's nothing to install. It's a portable folder you can keep anywhere, and the `README.txt` inside it explains the rest.

### Building it yourself

If you'd rather build it from the source code, read the build instructions for your system:
- **Windows:** [BuyTheBoat/build/BUILDING-windows.md](BuyTheBoat/build/BUILDING-windows.md)
- **Linux:** [BuyTheBoat/build/BUILDING-linux.md](BuyTheBoat/build/BUILDING-linux.md)

The process will create the same portable folder you'd get from the Releases page. You can delete the rest of this stuff once you've made that.

## Core concept to understand 

First understand the idea of "funds" this program uses. It's the main feature of this program.
You might have $2500 in your account, but if $1000 is going towards rent, $800 is going towards bills, you need $200 to replace the wind shield, and $50 is going towards a trip you want to take, you only have $450 free to actually spend.
This program virtually separates those out into "fund jars", like jars of cash that's been set aside for a specific expense.
After you enter any kind of expense (whether that's a regular bill or perhaps a boat you want to buy in a year), you have the option to "save and plan". This will let you fine tune the pace you want to set funds aside for that specific expense.
On any given day, this program will have tracked all the money that's been set aside. The rest of the money for that day is called "free funds", and it's how much you're free to spend on whatever else.

## How do I use the program once it's launched?

First time:

1. First add your bank accounts. You'll need their name and current balance. Unfortunately credit cards aren't considered an "account" by this program. Consider them either an expense or just add the expenses that come out of them into whatever account you pay the credit card with.
2. These next steps can be done in any order:
 - You'll first want to add your whatever regular income you have and your bills. 
 - You should also add any one-time expenses you anticipate coming up that you'll want to plan around.
3. Create savings plans for the expenses. This is done automatically for each bill. You can fine tune these savings plans any time for an expense. You can even make one time adjustments to move funds around manually. 
4. Go to the "forecast" tab and click the forecast button. It will create a little calendar overview that shows when money moves around and how much you're free to spend. Click on a day in the calendar to learn more.

Any subsequent time:
1. Manually update the bank accounts with how much you actually have in them. Yes, it's kind of annoying like that.
2. Use the forecast tab to see how things are doing.
3. Update stuff if you want.

### Updating the program

If you see there's an update available on the github page for this project and you want to install it, you'll first have to export your current data. It will make a database file. Hold onto this somewhere.
You can delete the entire folder for the old version of the program at this point. It won't hurt anything to keep it other than taking up space. 
Download the new version from the [Releases page](https://github.com/hwynn/BuyTheBoat/releases/latest) (or build it again yourself). Launch the new version, and from there, import that database file that was created.
You now have the latest version of the program. 