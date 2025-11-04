import axios from 'axios'
import crypto from 'node:crypto';
import {stringify} from 'node:querystring';

/**
 * 构造请求负载数据
 * @param {string} 目标查询单词或者句子
 * @param {string} 目标翻译语言
 * @returns {string} 构造好的负载数据字符串
 */
function buildPayload(query, targetLang) {
  const hFunc = (token) => {
    return crypto.createHash('md5').update(token.toString()).digest('hex')
  };
  let form = 'webdict';
  let time = ''.concat(query).concat(form).length % 10;

  let r = ''.concat(query).concat(form);
  let o = hFunc(r);
  let n = ''.concat('web')
              .concat(query)
              .concat(time)
              .concat('Mk6hqtUp33DGGtoS63tTJbMUYjRrG1Lu')
              .concat(o)
  let sig = hFunc(n);

  return stringify({
    q: query,
    le: targetLang,
    t: time,
    client: 'web',
    sign: sig,
    keyform: form
  });
}

async function query() {
  const headers = {
    'user-agent':
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0',
    'content-type': 'application/x-www-form-urlencoded;charset=utf-8'
  };
  let payload = buildPayload('我的世界', 'en');
  const response = await axios.post(
      'https://dict.youdao.com/jsonapi_s?doctype=json&jsonversion=4', payload,
      {headers: headers});

  console.log(response.data);
}

function main() {
  console.log('Hello Youdao!');
  query()
}
main();