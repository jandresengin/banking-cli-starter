"""Manual integration probe against the local demo; creates only simulated transactions."""
import json, urllib.request, http.cookiejar

url = 'http://localhost:5082/api/command'
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
def send(value):
    body = json.dumps({'input': value, 'provider': 'ollama'}).encode()
    with client.open(urllib.request.Request(url, data=body, headers={'Content-Type':'application/json'}), timeout=150) as response:
        return json.load(response)

cases = [
 ('US deposit', 'Please deposit 20 dollars into my checking account.', 'deposit chequing 20'),
 ('US withdrawal', 'Take 2 dollars out of my checking account.', 'withdraw chequing 2'),
 ('Canadian transfer', 'Move 3 dollars from chequing to savings.', 'transfer chequing savings 3'),
 ('Canadian balance', 'What is my chequing balance?', 'balance chequing'),
 ('UK deposit', 'Pay 10 dollars into my current account.', 'deposit chequing 10'),
 ('UK transfer', 'Transfer 4 dollars from my current account to savings.', 'transfer chequing savings 4'),
 ('French deposit', "Dépose 10 dollars dans mon compte d'épargne.", 'deposit savings 10'),
 ('French withdrawal', "Retire 2 dollars de mon compte d'épargne.", 'withdraw savings 2'),
 ('French transfer', "Transfère 3 dollars du compte courant vers le compte d'épargne.", 'transfer chequing savings 3'),
 ('Mixed transfer', "Move 2 dollars from mon compte d'épargne to chequing.", 'transfer savings chequing 2'),
 ('Mixed balance', "What is the balance de mon compte d'épargne?", 'balance savings'),
 ('Missing transfer account', 'Transfer 4 dollars to savings.', None),
]
for label, message, expected in cases:
    try:
        result = send(message)
        print(json.dumps({'case':label,'input':message,'expected':expected,'stage':result.get('stage'),'command':result.get('command'),'interpretation':result.get('aiInterpretation'),'output':result.get('output'),'simulator':result.get('simulatorResult')}, ensure_ascii=False), flush=True)
        if result.get('stage') == 'confirmation_required': send('no')
    except Exception as error:
        print(json.dumps({'case':label,'error':str(error)}),flush=True)
