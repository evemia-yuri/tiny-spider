import axios from 'axios'
import {writeFile} from 'fs/promises';
import crypto from 'node:crypto';
import {stringify} from 'node:querystring';

/**
 * Generate request payload data
 * @param {string} query target words or sentence
 * @param {string} target target translate language
 * @returns {string} payload string
 */
function buildPayload(query, lang) {
  const hFunc = (token) => {
    return crypto.createHash('md5').update(token.toString()).digest('hex')
  };
  const keyform = 'webdict';
  const time = ''.concat(query).concat(keyform).length % 10;
  const secret = 'Mk6hqtUp33DGGtoS63tTJbMUYjRrG1Lu';

  let r = ''.concat(query).concat(keyform);
  let o = hFunc(r);
  let n = ''.concat('web').concat(query).concat(time).concat(secret).concat(o);
  let sign = hFunc(n);

  return stringify({
    q: query,
    le: lang,
    t: time,
    client: 'web',
    sign: sign,
    keyform: keyform
  });
}

async function main() {
  const headers = {
    'user-agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) \
         AppleWebKit/537.36 (KHTML, like Gecko) \
         Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0',
    'content-type': 'application/x-www-form-urlencoded;charset=utf-8'
  };
  let payload = buildPayload('word', 'en');
  const response = await axios.post(
      'https://dict.youdao.com/jsonapi_s?doctype=json&jsonversion=4', payload,
      {headers: headers});

  const fileName = 'output.json';
  await writeFile(fileName, JSON.stringify(response.data, null, 2));
}

main();